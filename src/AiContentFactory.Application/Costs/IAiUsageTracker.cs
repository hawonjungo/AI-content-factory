namespace AiContentFactory.Application.Costs;

public record RecordUsageInput(
    string Provider,
    string Model,
    string Operation,
    decimal EstimatedCostUsd,
    Guid? ContentProjectId,
    Guid? SceneId);

/// <summary>
/// AI generation must never blindly consume credits (Section 16). Callers
/// check EnsureBudgetAvailableAsync BEFORE an expensive call (Veo/image/TTS)
/// and call RecordAsync AFTER a successful one. This is a monthly budget
/// only for MVP - per-video/per-scene budgets and a regeneration reserve
/// are natural follow-ups once real usage data exists.
/// </summary>
public interface IAiUsageTracker
{
    Task RecordAsync(RecordUsageInput input, CancellationToken cancellationToken = default);

    Task<decimal> GetCurrentMonthSpendAsync(CancellationToken cancellationToken = default);

    /// <summary>Throws BudgetExceededException if the current month's spend already meets/exceeds the configured monthly limit.</summary>
    Task EnsureBudgetAvailableAsync(CancellationToken cancellationToken = default);
}

public class BudgetExceededException : Exception
{
    public BudgetExceededException(decimal currentSpend, decimal monthlyLimit)
        : base($"Monthly AI budget exceeded: ${currentSpend:F2} spent of ${monthlyLimit:F2} limit. Generation stopped - raise Budget:MonthlyLimitUsd or wait for next month.")
    {
    }
}
