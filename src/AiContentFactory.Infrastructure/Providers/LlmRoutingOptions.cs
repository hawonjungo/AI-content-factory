namespace AiContentFactory.Infrastructure.Providers;

/// <summary>Which provider name to try first, and which (if any) to fall back to, for one pipeline step.</summary>
public class LlmRouteOptions
{
    /// <summary>"Gemini" or "Groq" (case-insensitive). Unrecognised or unconfigured names fall back to Gemini.</summary>
    public string Primary { get; set; } = "Gemini";

    /// <summary>Optional. Null/empty = no fallback for this step.</summary>
    public string? Fallback { get; set; }
}

/// <summary>
/// Bound from "Llm:Routing". Defaults to Gemini-only for every task (no
/// Fallback), so an app with no Groq key configured behaves exactly as before
/// - routing only changes behaviour once a second provider is actually set up.
/// </summary>
public class LlmRoutingOptions
{
    public const string SectionName = "Llm:Routing";

    public LlmRouteOptions Ideas { get; set; } = new();
    public LlmRouteOptions HookTitle { get; set; } = new();
    public LlmRouteOptions Script { get; set; } = new();
}
