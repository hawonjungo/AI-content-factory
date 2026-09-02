using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Publishing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Publishing;

public interface IPublishingService
{
    /// <summary>
    /// Validates the rendered video once, then creates one independent
    /// <see cref="PublishJob"/> per requested platform and hands it to the
    /// scheduler (now or at the scheduled time). Platforms already queued /
    /// published for this video are skipped, not duplicated.
    /// </summary>
    Task<PublishResponse> PublishAsync(Guid contentProjectId, PublishRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublishJobDto>> GetJobsAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task<PublishJobDto> RetryAsync(Guid contentProjectId, Guid publishJobId, CancellationToken cancellationToken = default);

    /// <summary>Runs one publish job against its platform. Called by the background worker; idempotent for an already-published job.</summary>
    Task RunJobAsync(Guid publishJobId, CancellationToken cancellationToken = default);
}

public class PublishingService : IPublishingService
{
    private readonly IContentProjectRepository _projects;
    private readonly IAssetService _assets;
    private readonly IFileStorage _fileStorage;
    private readonly IMediaProbe _mediaProbe;
    private readonly IPublishJobRepository _jobs;
    private readonly ISocialConnectionService _connections;
    private readonly IPublishJobScheduler _scheduler;
    private readonly IReadOnlyDictionary<PublishTarget, ISocialPlatformPublisher> _publishers;
    private readonly PublishingOptions _options;
    private readonly ILogger<PublishingService> _logger;

    public PublishingService(
        IContentProjectRepository projects,
        IAssetService assets,
        IFileStorage fileStorage,
        IMediaProbe mediaProbe,
        IPublishJobRepository jobs,
        ISocialConnectionService connections,
        IPublishJobScheduler scheduler,
        IEnumerable<ISocialPlatformPublisher> publishers,
        IOptions<PublishingOptions> options,
        ILogger<PublishingService> logger)
    {
        _projects = projects;
        _assets = assets;
        _fileStorage = fileStorage;
        _mediaProbe = mediaProbe;
        _jobs = jobs;
        _connections = connections;
        _scheduler = scheduler;
        _publishers = publishers.ToDictionary(p => p.Platform);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PublishResponse> PublishAsync(Guid contentProjectId, PublishRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var platforms = (request.Platforms ?? Array.Empty<PublishTarget>()).Distinct().ToList();
        var errors = new List<string>();

        if (platforms.Count == 0)
        {
            errors.Add("Chọn ít nhất một nền tảng để đăng.");
        }

        DateTimeOffset? scheduledAtUtc = null;
        if (request.Mode == PublishMode.Schedule)
        {
            if (request.ScheduledAtUtc is not { } at)
            {
                errors.Add("Chọn thời điểm đăng khi dùng chế độ Lên lịch.");
            }
            else if (at <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                errors.Add("Thời điểm lên lịch phải ở tương lai.");
            }
            else
            {
                scheduledAtUtc = at.ToUniversalTime();
            }
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add("Tiêu đề không được để trống.");
        }

        var video = await ResolveFinalVideoAsync(contentProjectId, cancellationToken);
        MediaInfo probe = MediaInfo.Unreadable("not probed");
        long sizeBytes = 0;

        if (video is null)
        {
            errors.Add("Chưa có video hoàn chỉnh - hãy ghép video ở Bước 6 trước.");
        }
        else
        {
            var absolutePath = _fileStorage.GetAbsolutePath(video.FilePath!);
            sizeBytes = await GetVideoSizeAsync(video.FilePath!, cancellationToken);
            probe = await _mediaProbe.ProbeAsync(absolutePath, cancellationToken);
            errors.AddRange(PublishVideoValidator.Validate(video.FilePath, probe, sizeBytes));
        }

        if (errors.Count > 0)
        {
            throw new PublishValidationException(errors);
        }

        var existingByPlatform = (await _jobs.GetByProjectAsync(contentProjectId, cancellationToken))
            .GroupBy(j => j.Platform)
            .ToDictionary(g => g.Key, g => g.ToList());
        var connectedPlatforms = (await _connections.GetStatusesAsync(cancellationToken))
            .Where(c => c.Status == nameof(Domain.Publishing.SocialConnectionStatus.Connected))
            .Select(c => c.Platform)
            .ToHashSet();

        var created = new List<PublishJobDto>();
        var skipped = new List<PublishSkip>();

        foreach (var platform in platforms)
        {
            var publisher = _publishers.GetValueOrDefault(platform);
            if (publisher is null || !publisher.IsConfigured)
            {
                skipped.Add(new PublishSkip(platform.ToString(), "Nền tảng chưa được cấu hình (thiếu client key/secret)."));
                continue;
            }

            if (!connectedPlatforms.Contains(platform.ToString()))
            {
                skipped.Add(new PublishSkip(platform.ToString(), "Tài khoản chưa được kết nối - kết nối ở Bước 7 trước."));
                continue;
            }

            // Platform-specific video constraints (duration/size ceilings differ per platform).
            var constraint = publisher.ValidateVideo(probe!, sizeBytes);
            if (!constraint.Ok)
            {
                skipped.Add(new PublishSkip(platform.ToString(), string.Join(" ", constraint.Errors)));
                continue;
            }

            // Idempotency: an active (queued / scheduled / running / published) job
            // for this same video means a re-publish would be a duplicate.
            if (existingByPlatform.TryGetValue(platform, out var priors) && priors.Any(j => j.IsActive && j.VideoAssetId == video!.Id))
            {
                var active = priors.First(j => j.IsActive && j.VideoAssetId == video!.Id);
                skipped.Add(new PublishSkip(platform.ToString(),
                    active.Status == PublishJobStatus.Published
                        ? "Video này đã được đăng lên nền tảng này."
                        : "Đã có một tác vụ đăng đang chờ cho video này."));
                continue;
            }

            var job = PublishJob.Create(
                contentProjectId,
                platform,
                video!.Id,
                video.FilePath!,
                request.Title,
                request.Caption,
                request.Hashtags,
                scheduledAtUtc);

            await _jobs.AddAsync(job, cancellationToken);
            created.Add(PublishJobDto.FromDomain(job));
        }

        await _jobs.SaveChangesAsync(cancellationToken);

        // Hand off only after the rows are committed, so a worker that picks the
        // job up immediately always finds it.
        foreach (var dto in created)
        {
            if (scheduledAtUtc is { } at)
            {
                _scheduler.Schedule(dto.Id, at);
            }
            else
            {
                _scheduler.EnqueueNow(dto.Id);
            }
        }

        if (created.Count > 0)
        {
            _logger.LogInformation(
                "Queued {Count} publish job(s) for ContentProject {ContentProjectId} ({Mode}): {Platforms}",
                created.Count, contentProjectId, scheduledAtUtc is null ? "now" : $"scheduled {scheduledAtUtc}",
                string.Join(", ", created.Select(c => c.Platform)));
        }

        return new PublishResponse(created, skipped);
    }

    public async Task<IReadOnlyList<PublishJobDto>> GetJobsAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var jobs = await _jobs.GetByProjectAsync(contentProjectId, cancellationToken);
        return jobs.OrderByDescending(j => j.CreatedAt).Select(PublishJobDto.FromDomain).ToList();
    }

    public async Task<PublishJobDto> RetryAsync(Guid contentProjectId, Guid publishJobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(publishJobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw new DomainException("Không tìm thấy tác vụ đăng này.");
        }

        if (!job.CanRetry)
        {
            throw new DomainException(job.IsPermanentFailure
                ? "Tác vụ này lỗi vĩnh viễn, không thể thử lại (sửa đầu vào rồi tạo tác vụ mới)."
                : "Chỉ có thể thử lại tác vụ đang ở trạng thái Lỗi.");
        }

        job.ResetForRetry();
        await _jobs.SaveChangesAsync(cancellationToken);

        if (job.ScheduledAtUtc is { } at && at > DateTimeOffset.UtcNow)
        {
            _scheduler.Schedule(job.Id, at);
        }
        else
        {
            _scheduler.EnqueueNow(job.Id);
        }

        return PublishJobDto.FromDomain(job);
    }

    public async Task RunJobAsync(Guid publishJobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobs.GetByIdAsync(publishJobId, cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("Publish job {JobId} no longer exists - skipping", publishJobId);
            return;
        }

        // Idempotency: never post twice. A Hangfire retry or a double-enqueue
        // that lands after the job already completed is a no-op.
        if (job.Status is PublishJobStatus.Published or PublishJobStatus.Cancelled)
        {
            _logger.LogInformation("Publish job {JobId} is already {Status} - nothing to do", publishJobId, job.Status);
            return;
        }

        var publisher = _publishers.GetValueOrDefault(job.Platform);
        if (publisher is null || !publisher.IsConfigured)
        {
            job.MarkFailed($"{job.Platform} chưa được cấu hình.", permanent: true);
            await _jobs.SaveChangesAsync(cancellationToken);
            return;
        }

        job.MarkPublishing();
        await _jobs.SaveChangesAsync(cancellationToken);

        try
        {
            var token = await _connections.GetUsableAccessTokenAsync(job.Platform, cancellationToken);

            var absolutePath = _fileStorage.GetAbsolutePath(job.VideoPath);
            var size = await GetVideoSizeAsync(job.VideoPath, cancellationToken);
            var probe = await _mediaProbe.ProbeAsync(absolutePath, cancellationToken);

            var hardErrors = PublishVideoValidator.Validate(job.VideoPath, probe, size);
            var constraint = publisher.ValidateVideo(probe, size);
            if (hardErrors.Count > 0 || !constraint.Ok)
            {
                throw new PublishException(
                    string.Join(" ", hardErrors.Concat(constraint.Errors)), retryable: false);
            }

            var publicUrl = _options.HasPublicBaseUrl
                ? $"{_options.PublicBaseUrl.TrimEnd('/')}/content-projects/{job.ContentProjectId}/assets/{job.VideoAssetId}/file"
                : null;

            var result = await publisher.UploadAsync(new PublishUploadRequest(
                new PublishMetadata(job.Title, job.FullDescription),
                new PublishVideo(
                    OpenStream: ct => _fileStorage.GetAsync(job.VideoPath, ct),
                    SizeBytes: size,
                    Probe: probe,
                    PublicUrl: publicUrl),
                token.AccessToken,
                token.AccountId,
                job.ScheduledAtUtc),
                cancellationToken);

            job.MarkPublished(result.ExternalId, result.Url);
            await _jobs.SaveChangesAsync(cancellationToken);
            await MarkProjectPublishedIfAllDoneAsync(job.ContentProjectId, cancellationToken);

            _logger.LogInformation(
                "Published ContentProject {ContentProjectId} to {Platform}: {ExternalId} {Url}",
                job.ContentProjectId, job.Platform, result.ExternalId, result.Url);
        }
        catch (PublishException ex)
        {
            job.MarkFailed(ex.Message, permanent: !ex.Retryable);
            await _jobs.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(ex, "Publish job {JobId} ({Platform}) failed (retryable={Retryable})", job.Id, job.Platform, ex.Retryable);

            // Rethrow only when a later retry could still succeed AND we are under
            // the attempt cap - that lets the background system retry it.
            if (ex.Retryable && job.AttemptCount < PublishJob.MaxAttempts)
            {
                throw;
            }
        }
        catch (Exception ex)
        {
            // Unexpected error - treat as transient the first few times.
            var underCap = job.AttemptCount < PublishJob.MaxAttempts;
            job.MarkFailed(ex.Message, permanent: !underCap);
            await _jobs.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Publish job {JobId} ({Platform}) threw", job.Id, job.Platform);
            if (underCap)
            {
                throw;
            }
        }
    }

    private async Task MarkProjectPublishedIfAllDoneAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var jobs = await _jobs.GetByProjectAsync(contentProjectId, cancellationToken);
        if (jobs.Count == 0 || jobs.Any(j => j.Status is not (PublishJobStatus.Published or PublishJobStatus.Cancelled)))
        {
            return;
        }

        var project = await _projects.GetByIdAsync(contentProjectId, cancellationToken);
        if (project is null || project.Status == ContentProjectStatus.Published)
        {
            return;
        }

        try
        {
            if (project.Status == ContentProjectStatus.AwaitingApproval)
            {
                project.TransitionTo(ContentProjectStatus.Approved);
            }
            if (project.Status == ContentProjectStatus.Approved)
            {
                project.TransitionTo(ContentProjectStatus.Published);
                await _projects.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DomainException ex)
        {
            // Publishing succeeded; the status bookkeeping is best-effort.
            _logger.LogDebug(ex, "Could not move ContentProject {ContentProjectId} to Published", contentProjectId);
        }
    }

    private async Task<AssetResponse?> ResolveFinalVideoAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var assets = await _assets.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        return assets
            .Where(a => a.SceneId is null
                && a.Type == nameof(AssetType.Video)
                && a.Status == nameof(AssetStatus.Ready)
                && !string.IsNullOrWhiteSpace(a.FilePath))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<long> GetVideoSizeAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _fileStorage.GetAsync(storageKey, cancellationToken);
            if (stream.CanSeek)
            {
                return stream.Length;
            }

            // Non-seekable stream: fall back to counting bytes (rare - local + memory both seek).
            var total = 0L;
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
