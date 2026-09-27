namespace AiContentFactory.Application.Providers;

/// <summary>
/// The LLM provider cut its own response short before finishing (Gemini's
/// finishReason "MAX_TOKENS") - the response is usually incomplete/invalid
/// JSON as a direct result, not because the model produced bad output.
///
/// Distinct from a generic parse failure so this is diagnosable (a log line
/// naming this exception is far more actionable than "invalid JSON: expected
/// end of string") and so a caller could choose to react differently (e.g.
/// retry once with a shorter prompt) if that's ever worth doing. Today,
/// JsonAgentRunner's existing repair-retry already gives one more attempt,
/// which is usually enough since token budgets are configured with headroom.
/// </summary>
public class LlmResponseTruncatedException : Exception
{
    public LlmResponseTruncatedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
