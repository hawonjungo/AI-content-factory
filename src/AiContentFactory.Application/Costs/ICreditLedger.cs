using AiContentFactory.Domain.Generation;

namespace AiContentFactory.Application.Costs;

/// <param name="Tier">Required for <see cref="GenerationKind.Video"/>; ignored otherwise.</param>
public record CreditReservationRequest(
    Guid ContentProjectId,
    Guid? SceneId,
    GenerationKind Kind,
    string Provider,
    string Model,
    VideoModelTier? Tier = null);

/// <summary>
/// The single authority on the daily AI-generation credit budget. Everything
/// that spends credits goes through here in this order:
///
///   1. caller validates the storyboard / prompts / required assets
///   2. <see cref="ReserveAsync"/>  - checks the retry cap, checks the budget,
///      records a Pending/Generating <see cref="GenerationAttempt"/>
///   3. caller runs the provider
///   4. <see cref="CommitAsync"/> on success (records actual credits + asset),
///      or <see cref="FailAsync"/> on error (records the failure; no credits
///      are counted against the budget for a failed attempt)
///   5. optional <see cref="MarkValidatedAsync"/> once post-generation checks pass
///
/// Reserved-but-unfinished attempts count against the budget too, so two
/// concurrent runs can't both pass the check and then overspend.
/// </summary>
/// <param name="Reserved">Credits held by attempts still running (Pending / Generating / Retrying).</param>
/// <param name="Used">Credits actually charged by finished attempts (Completed / Validated) today.</param>
/// <param name="FailedToday">Failed attempts today - shown so the UI never hides them; they cost no credits.</param>
public record CreditUsageSummary(
    int DailyBudget,
    int Reserved,
    int Used,
    int Remaining,
    int FailedToday);

public interface ICreditLedger
{
    /// <summary>Credits left in today's account-wide pool (budget minus committed minus still-reserved).</summary>
    Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default);

    /// <summary>The full daily breakdown for display: budget / reserved / used / remaining / failed.</summary>
    Task<CreditUsageSummary> GetDailyUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="CreditBudgetExceededException"/> when <paramref name="requiredCredits"/> won't fit in what's left today.</summary>
    Task EnsureAvailableAsync(int requiredCredits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves credits for one generation and returns the attempt row.
    /// Idempotent per scene+kind: if a Completed/Validated attempt already
    /// exists it is returned as-is and nothing new is reserved. Throws
    /// <see cref="RetryLimitExceededException"/> once the failed attempts for a
    /// scene+kind reach <see cref="CreditCostOptions.MaxAttemptsPerScene"/>, and
    /// <see cref="CreditBudgetExceededException"/> when the budget can't cover it.
    /// </summary>
    Task<GenerationAttempt> ReserveAsync(CreditReservationRequest request, CancellationToken cancellationToken = default);

    Task CommitAsync(Guid attemptId, int actualCredits, Guid? assetId = null, double? audioDurationSeconds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records credits a scene consumed OUTSIDE the app - e.g. the user
    /// generated the clip in Google Flow and imported it. Idempotent per
    /// scene+kind, and NOT gated by the daily budget: the credits were already
    /// spent in Flow, this only makes the ledger's used/remaining reflect that.
    /// </summary>
    Task<GenerationAttempt> RecordExternalCompletionAsync(CreditReservationRequest request, int actualCredits, Guid? assetId = null, CancellationToken cancellationToken = default);

    Task MarkValidatedAsync(Guid attemptId, CancellationToken cancellationToken = default);

    Task FailAsync(Guid attemptId, string reason, CancellationToken cancellationToken = default);
}

public class CreditBudgetExceededException : Exception
{
    public CreditBudgetExceededException(int requiredCredits, int remainingCredits, int dailyBudget)
        : base($"Daily credit budget exceeded: this step needs {requiredCredits} credit(s) but only {remainingCredits} of {dailyBudget} remain today. Generation stopped - raise Credits:DailyBudgetCredits or wait for the UTC-day reset.")
    {
        RequiredCredits = requiredCredits;
        RemainingCredits = remainingCredits;
        DailyBudget = dailyBudget;
    }

    public int RequiredCredits { get; }
    public int RemainingCredits { get; }
    public int DailyBudget { get; }
}

public class RetryLimitExceededException : Exception
{
    public RetryLimitExceededException(Guid? sceneId, string kind, int attempts, int maxAttempts)
        : base($"Retry limit reached for {kind} on scene {sceneId?.ToString() ?? "(project)"}: {attempts} attempt(s), max {maxAttempts}. Not spending more credits on this generation - inspect the recorded failures first.")
    {
        Attempts = attempts;
        MaxAttempts = maxAttempts;
    }

    public int Attempts { get; }
    public int MaxAttempts { get; }
}
