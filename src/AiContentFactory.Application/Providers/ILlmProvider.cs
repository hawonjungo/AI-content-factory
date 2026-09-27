namespace AiContentFactory.Application.Providers;

public interface ILlmProvider
{
    /// <summary>False when this provider's API key isn't configured - callers (the router) must never route to it and pretend it's available.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Generates text from a system instruction + user prompt. Implementations
    /// should request structured (JSON) output from the underlying model when
    /// the provider supports it, since callers (the content agents) always
    /// parse the result as JSON.
    /// </summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Which pipeline step is asking for text, so <see cref="ILlmRouter"/> can pick a
/// different primary/fallback provider per step (e.g. a cheap/fast model for
/// short idea/hook output, a higher-quality one for full scripts) instead of
/// every agent hard-wiring one provider.
/// </summary>
public enum LlmTaskType
{
    /// <summary>Step 2 content idea shortlist - short output, called often.</summary>
    Ideas,
    /// <summary>Short hook/title-style generation - short output, called often.</summary>
    HookTitle,
    /// <summary>Full narration script - longer output, quality matters most here.</summary>
    Script
}

/// <summary>
/// Resolves an <see cref="ILlmProvider"/> for one pipeline step, per
/// configuration (Llm:Routing:{TaskType}:Primary/Fallback). Centralizes
/// provider selection and fallback so agents never duplicate this logic or
/// pick a provider unconfigured for the caller's account - falls back to
/// Gemini (the always-required provider) when a configured route points at a
/// provider that isn't actually set up.
/// </summary>
public interface ILlmRouter
{
    /// <summary>Returns a provider (or a primary-with-fallback wrapper) ready to call for this task - callers use it exactly like any other <see cref="ILlmProvider"/>.</summary>
    ILlmProvider Resolve(LlmTaskType task);
}
