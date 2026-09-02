namespace AiContentFactory.Application.Providers;

/// <summary>
/// Tracks the daily free-credit pool (Google Flow tier). The pool is
/// ACCOUNT-WIDE and resets at 00:00 UTC - it is not per project. Informational
/// only: nothing is stopped by it anymore, since Veo actually bills real USD
/// per second generated (see <see cref="AiContentFactory.Application.Costs.PricingOptions.VideoUsdPerSecond"/>).
/// </summary>
public interface IGoogleFlowQuotaManager
{
    Task<QuotaCheckResult> CheckDailyQuotaAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>Credits left in today's account-wide pool.</summary>
    Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default);

    /// <summary>Charges one AI video clip's worth of credits (throws if the pool can't cover it).</summary>
    Task RecordVideoGenerationAsync(
        Guid projectId,
        Guid? sceneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Charges one AI still image's worth of credits.</summary>
    Task RecordImageGenerationAsync(
        Guid projectId,
        Guid? sceneId = null,
        CancellationToken cancellationToken = default);

    Task<QuotaSummary> GetTodaysSummaryAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task ResetDailyQuotaAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

public record QuotaCheckResult(
    bool CanGenerate,
    int RemainingCredits,
    int VideosGeneratedToday,
    int MaxVideosPerDay,
    DateTime ResetTime);

public record QuotaSummary(
    int TotalCredits,
    int UsedCredits,
    int RemainingCredits,
    int VideosGeneratedToday,
    List<GoogleFlowVideoGenerationRecord> GenerationHistory);

public record GoogleFlowVideoGenerationRecord(
    Guid Id,
    Guid ProjectId,
    DateTime GeneratedAt,
    int CreditsUsed,
    string SceneId,
    bool Success,
    string? ErrorMessage);

/// <summary>
/// Budget exceeded exception specifically for Google Flow quota (not monthly USD budget).
/// </summary>
public class GoogleFlowQuotaExceededException : Exception
{
    public GoogleFlowQuotaExceededException(string message, int remainingCredits, int videosGenerated)
        : base(message)
    {
        RemainingCredits = remainingCredits;
        VideosGenerated = videosGenerated;
    }

    public int RemainingCredits { get; }
    public int VideosGenerated { get; }
}
