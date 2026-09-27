using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Publishing;

/// <summary>
/// One rendered video being published to one platform. Publishing the same
/// video to three platforms creates three of these; each succeeds or fails on
/// its own. Carries everything the scheduler/worker needs so a job can run long
/// after the request that created it: the video reference, the caption/hashtags,
/// the scheduled time, the lifecycle state, the external post id + URL on
/// success, the error + retry bookkeeping on failure.
/// </summary>
public class PublishJob : BaseEntity
{
    /// <summary>Hard cap on total attempts (first try + retries) before a job is treated as permanently failed.</summary>
    public const int MaxAttempts = 4;

    public Guid ContentProjectId { get; private set; }
    public PublishTarget Platform { get; private set; }

    /// <summary>The final rendered video asset, and its storage key (so the worker can read the bytes).</summary>
    public Guid VideoAssetId { get; private set; }
    public string VideoPath { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;
    public string Caption { get; private set; } = string.Empty;
    /// <summary>Space-joined, normalised "#tag #tag" string. Empty when none.</summary>
    public string Hashtags { get; private set; } = string.Empty;

    /// <summary>
    /// The platform-specific privacy/visibility value chosen at publish time
    /// (e.g. TikTok's "SELF_ONLY", YouTube's "unlisted"). Null = the platform's
    /// own default. Interpreted only by that platform's publisher - never
    /// shared across platforms.
    /// </summary>
    public string? Privacy { get; private set; }

    /// <summary>Null = publish now. Otherwise the UTC instant the scheduler should run this.</summary>
    public DateTimeOffset? ScheduledAtUtc { get; private set; }

    public PublishJobStatus Status { get; private set; } = PublishJobStatus.Pending;

    public string? ExternalPostId { get; private set; }
    public string? PublishedUrl { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public string? ErrorMessage { get; private set; }
    public int AttemptCount { get; private set; }
    /// <summary>True when the failure will never succeed on retry (bad request, unsupported video, revoked auth) or the attempt cap was hit.</summary>
    public bool IsPermanentFailure { get; private set; }

    /// <summary>
    /// Stable key for the (project, platform, video) triple. A second publish
    /// request with the same key while a job is still active is a no-op, so a
    /// double-click or a job retry never creates a duplicate post.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    private PublishJob()
    {
        // EF Core
    }

    public static PublishJob Create(
        Guid contentProjectId,
        PublishTarget platform,
        Guid videoAssetId,
        string videoPath,
        string title,
        string? caption,
        string? hashtags,
        DateTimeOffset? scheduledAtUtc,
        string? privacy = null)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            throw new DomainException("A rendered video is required before publishing.");
        }

        return new PublishJob
        {
            ContentProjectId = contentProjectId,
            Platform = platform,
            VideoAssetId = videoAssetId,
            VideoPath = videoPath.Trim(),
            Title = (title ?? string.Empty).Trim(),
            Caption = (caption ?? string.Empty).Trim(),
            Hashtags = NormalizeHashtags(hashtags),
            ScheduledAtUtc = scheduledAtUtc,
            Privacy = string.IsNullOrWhiteSpace(privacy) ? null : privacy.Trim(),
            Status = scheduledAtUtc is null ? PublishJobStatus.Pending : PublishJobStatus.Scheduled,
            IdempotencyKey = $"{contentProjectId:N}:{platform}:{videoAssetId:N}"
        };
    }

    /// <summary>Active = queued, scheduled, running, or already published - i.e. a re-publish would be a duplicate.</summary>
    public bool IsActive =>
        Status is PublishJobStatus.Pending or PublishJobStatus.Scheduled or PublishJobStatus.Publishing or PublishJobStatus.Published;

    public bool CanRetry =>
        Status == PublishJobStatus.Failed && !IsPermanentFailure && AttemptCount < MaxAttempts;

    public bool IsTerminal =>
        Status is PublishJobStatus.Published or PublishJobStatus.Cancelled
        || (Status == PublishJobStatus.Failed && IsPermanentFailure);

    /// <summary>Caption + hashtags combined the way most platforms want a single description field.</summary>
    public string FullDescription =>
        string.IsNullOrWhiteSpace(Hashtags)
            ? Caption
            : string.IsNullOrWhiteSpace(Caption) ? Hashtags : $"{Caption}\n\n{Hashtags}";

    public void MarkPublishing()
    {
        if (Status == PublishJobStatus.Published)
        {
            return; // idempotent - never re-run a completed job
        }

        Status = PublishJobStatus.Publishing;
        AttemptCount++;
        ErrorMessage = null;
        Touch();
    }

    public void MarkPublished(string externalPostId, string? publishedUrl)
    {
        ExternalPostId = string.IsNullOrWhiteSpace(externalPostId) ? null : externalPostId.Trim();
        PublishedUrl = string.IsNullOrWhiteSpace(publishedUrl) ? null : publishedUrl.Trim();
        PublishedAtUtc = DateTimeOffset.UtcNow;
        ErrorMessage = null;
        Status = PublishJobStatus.Published;
        Touch();
    }

    public void MarkFailed(string reason, bool permanent)
    {
        ErrorMessage = Truncate(string.IsNullOrWhiteSpace(reason) ? "unknown error" : reason.Trim(), 1000);
        IsPermanentFailure = permanent || AttemptCount >= MaxAttempts;
        Status = PublishJobStatus.Failed;
        Touch();
    }

    public void ResetForRetry()
    {
        if (!CanRetry)
        {
            throw new DomainException("This publish job cannot be retried.");
        }

        ErrorMessage = null;
        Status = ScheduledAtUtc is not null && ScheduledAtUtc > DateTimeOffset.UtcNow
            ? PublishJobStatus.Scheduled
            : PublishJobStatus.Pending;
        Touch();
    }

    public void Cancel()
    {
        if (Status == PublishJobStatus.Published)
        {
            throw new DomainException("A published job cannot be cancelled.");
        }

        Status = PublishJobStatus.Cancelled;
        Touch();
    }

    private static string NormalizeHashtags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var tags = raw
            .Split(new[] { ' ', ',', '\n', '\r', '\t', '#' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Select(t => "#" + t.TrimStart('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(" ", tags);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
