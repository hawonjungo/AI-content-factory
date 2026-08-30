namespace AiContentFactory.Application.Providers;

public interface ILlmProvider
{
    /// <summary>
    /// Generates text from a system instruction + user prompt. Implementations
    /// should request structured (JSON) output from the underlying model when
    /// the provider supports it, since callers (the content agents) always
    /// parse the result as JSON.
    /// </summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
