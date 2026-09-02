using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Costs;
using AiContentFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers;

/// <summary>
/// Tracks the account-wide free-credit pool that Google's Flow tier refills
/// each UTC day. Credit costs per generation come from
/// <see cref="GoogleFlowOptions"/>.
///
/// Deliberately NOT per-project: the 50/day allowance is shared across every
/// video the account makes, so the "remaining" figure ignores projectId even
/// though each usage row still records which project spent it.
/// </summary>
public class GoogleFlowQuotaManager : IGoogleFlowQuotaManager
{
    private readonly AppDbContext _dbContext;
    private readonly GoogleFlowOptions _options;
    private readonly ILogger<GoogleFlowQuotaManager> _logger;

    public GoogleFlowQuotaManager(
        AppDbContext dbContext,
        IOptions<GoogleFlowOptions> options,
        ILogger<GoogleFlowQuotaManager> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    private int DailyCredits => _options.DailyCredits;
    private int CreditsPerVideo => _options.CreditsPerVideoClip;
    private int CreditsPerImage => _options.CreditsPerImage;
    private int MaxVideosPerDay => CreditsPerVideo > 0 ? DailyCredits / CreditsPerVideo : 0;

    public async Task<QuotaCheckResult> CheckDailyQuotaAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var (used, count) = await UsedTodayAsync(cancellationToken);
        var remaining = DailyCredits - used;

        return new QuotaCheckResult(
            CanGenerate: remaining >= CreditsPerVideo,
            RemainingCredits: remaining,
            VideosGeneratedToday: count,
            MaxVideosPerDay: MaxVideosPerDay,
            ResetTime: GetTodayUtc().AddDays(1));
    }

    public async Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default)
    {
        var (used, _) = await UsedTodayAsync(cancellationToken);
        return DailyCredits - used;
    }

    public Task RecordVideoGenerationAsync(Guid projectId, Guid? sceneId = null, CancellationToken cancellationToken = default) =>
        RecordAsync(projectId, sceneId, CreditsPerVideo, "video", cancellationToken);

    public Task RecordImageGenerationAsync(Guid projectId, Guid? sceneId = null, CancellationToken cancellationToken = default) =>
        RecordAsync(projectId, sceneId, CreditsPerImage, "image", cancellationToken);

    // Records usage for display only - never enforced. Veo bills real USD per
    // second generated (IAiUsageTracker's monthly budget is the real guard),
    // not this free-credit pool.
    private async Task RecordAsync(Guid projectId, Guid? sceneId, int credits, string kind, CancellationToken cancellationToken)
    {
        _dbContext.Set<VideoGenerationRecord>().Add(new VideoGenerationRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            GeneratedAt = DateTime.UtcNow,
            CreditsUsed = credits,
            SceneId = (sceneId ?? Guid.Empty).ToString(),
            Success = true,
            ErrorMessage = kind
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Google Flow {Kind} for {ProjectId}: -{Credits} credits", kind, projectId, credits);
    }

    public async Task<QuotaSummary> GetTodaysSummaryAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var today = GetTodayUtc();
        var recordsToday = await _dbContext.Set<VideoGenerationRecord>()
            .Where(r => r.GeneratedAt >= today)
            .ToListAsync(cancellationToken);

        var usedCredits = recordsToday.Where(r => r.Success).Sum(r => r.CreditsUsed);

        var history = recordsToday.Select(r => new GoogleFlowVideoGenerationRecord(
            r.Id, r.ProjectId, r.GeneratedAt, r.CreditsUsed, r.SceneId, r.Success, r.ErrorMessage)).ToList();

        return new QuotaSummary(
            TotalCredits: DailyCredits,
            UsedCredits: usedCredits,
            RemainingCredits: DailyCredits - usedCredits,
            VideosGeneratedToday: recordsToday.Count(r => r.Success && r.ErrorMessage == "video"),
            GenerationHistory: history);
    }

    public async Task ResetDailyQuotaAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var today = GetTodayUtc();
        var recordsToday = await _dbContext.Set<VideoGenerationRecord>()
            .Where(r => r.GeneratedAt >= today)
            .ToListAsync(cancellationToken);

        _dbContext.Set<VideoGenerationRecord>().RemoveRange(recordsToday);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Daily credit pool reset - removed {Count} records", recordsToday.Count);
    }

    private async Task<(int UsedCredits, int VideoCount)> UsedTodayAsync(CancellationToken cancellationToken)
    {
        var today = GetTodayUtc();
        var recordsToday = await _dbContext.Set<VideoGenerationRecord>()
            .Where(r => r.GeneratedAt >= today && r.Success)
            .Select(r => new { r.CreditsUsed, r.ErrorMessage })
            .ToListAsync(cancellationToken);

        return (recordsToday.Sum(r => r.CreditsUsed), recordsToday.Count(r => r.ErrorMessage == "video"));
    }

    private static DateTime GetTodayUtc() => DateTime.UtcNow.Date;
}
