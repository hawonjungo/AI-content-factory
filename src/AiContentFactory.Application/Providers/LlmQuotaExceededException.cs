namespace AiContentFactory.Application.Providers;

/// <summary>
/// The LLM provider rejected the call because a quota, rate limit, or billing
/// spend cap was hit (typically HTTP 429 / RESOURCE_EXHAUSTED).
///
/// Distinct from a transient failure or a bad model response: retrying now will
/// not help and only wastes another potentially billable attempt. Callers should
/// surface a clear "AI budget/credit limit reached" message rather than a
/// generic failure.
/// </summary>
public class LlmQuotaExceededException : Exception
{
    public LlmQuotaExceededException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
