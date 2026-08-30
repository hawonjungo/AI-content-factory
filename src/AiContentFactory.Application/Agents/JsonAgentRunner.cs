using System.Text.Json;
using System.Text.Json.Serialization;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Agents;

/// <summary>
/// All content agents follow the same shape: call the LLM, parse strict
/// JSON, validate it against the expected DTO. This centralizes the
/// "validate -> repair/retry -> fail gracefully" policy from the
/// architecture doc so each agent doesn't reimplement it.
/// </summary>
internal static class JsonAgentRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<TOutput> RunAsync<TOutput>(
        ILlmProvider llmProvider,
        string systemPrompt,
        string userPrompt,
        Func<TOutput, bool> validate,
        CancellationToken cancellationToken)
    {
        var (result, error) = await TryGenerateAsync<TOutput>(llmProvider, systemPrompt, userPrompt, validate, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        // One repair attempt: tell the model exactly what was wrong and ask again.
        var repairPrompt = $"{userPrompt}\n\nYour previous response was invalid ({error}). " +
                            "Return ONLY valid JSON matching the requested schema - no markdown, no commentary.";

        var (retryResult, retryError) = await TryGenerateAsync<TOutput>(llmProvider, systemPrompt, repairPrompt, validate, cancellationToken);
        if (retryResult is not null)
        {
            return retryResult;
        }

        throw new AgentGenerationException($"LLM output failed validation after retry: {retryError}");
    }

    private static async Task<(TOutput? Result, string? Error)> TryGenerateAsync<TOutput>(
        ILlmProvider llmProvider,
        string systemPrompt,
        string userPrompt,
        Func<TOutput, bool> validate,
        CancellationToken cancellationToken)
    {
        string raw;
        try
        {
            raw = await llmProvider.GenerateAsync(systemPrompt, userPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            return (default, $"provider call failed: {ex.Message}");
        }

        TOutput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TOutput>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (default, $"invalid JSON: {ex.Message}");
        }

        if (parsed is null)
        {
            return (default, "response deserialized to null");
        }

        if (!validate(parsed))
        {
            return (default, "response failed schema validation");
        }

        return (parsed, null);
    }
}
