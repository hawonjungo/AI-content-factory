namespace AiContentFactory.Infrastructure.Providers.Groq;

/// <summary>
/// Bound from "Llm:Groq". Optional - the app runs fine on Gemini alone if this
/// is left unconfigured (empty ApiKey). Groq hosts open-weight models on its
/// own inference hardware with a genuinely free rate-limited tier - see
/// https://console.groq.com/docs/rate-limits for your account's current
/// limits before relying on it for production volume; Groq does not publish
/// one universal free-tier number and it can change per account/model.
///
/// Groq's model catalog varies by account and changes over time (e.g.
/// llama-3.3-70b-versatile was removed from some accounts) - if GroqLlmProvider
/// starts logging "model_not_found", list what's actually available to your
/// key with `GET {BaseUrl}/models` (Bearer your key) and update Model below,
/// rather than assuming any hard-coded name still exists.
/// </summary>
public class GroqOptions
{
    public const string SectionName = "Llm:Groq";

    /// <summary>
    /// Optional. Set via Llm__Groq__ApiKey (env var) or user-secrets - never
    /// commit a real key. Empty = Groq is treated as not configured and the
    /// router falls back to Gemini for every task.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Verify current model availability/naming against Groq's docs
    /// (console.groq.com/docs/models) before relying on this - Groq
    /// deprecates/renames models independently of this codebase. Confirmed
    /// available (with json_mode support) via a live /models call as of
    /// 2026-09-14 - no general-purpose Llama chat model was available on that
    /// account at that time, so this defaults to an OSS model instead.
    /// </summary>
    public string Model { get; set; } = "openai/gpt-oss-120b";

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Same rationale as GeminiOptions.MaxOutputTokens - every current caller needs a few hundred tokens at most.</summary>
    public int? MaxOutputTokens { get; set; } = 2048;
}
