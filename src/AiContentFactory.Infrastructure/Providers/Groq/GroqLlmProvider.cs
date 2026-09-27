using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Groq;

/// <summary>
/// Calls Groq's OpenAI-compatible chat completions endpoint directly over
/// HTTP (no SDK dependency, matching <see cref="Gemini.GeminiLlmProvider"/>'s
/// shape). Used as the cheap/fast option in <see cref="ILlmRouter"/> routes -
/// Groq's own inference hardware is materially faster than typical hosted
/// inference, and its free tier costs nothing for the request volumes this
/// app makes (see GroqOptions doc comment for the "verify current limits"
/// caveat - Groq does not publish one fixed number).
///
/// Entirely optional: if <see cref="GroqOptions.ApiKey"/> is empty,
/// <see cref="IsConfigured"/> is false and <see cref="ILlmRouter"/> skips this
/// provider - the app works exactly as before on Gemini alone.
/// </summary>
public class GroqLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly GroqOptions _options;
    private readonly ILogger<GroqLlmProvider> _logger;

    public GroqLlmProvider(HttpClient httpClient, IOptions<GroqOptions> options, ILogger<GroqLlmProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Groq API key is not configured. Set Llm__Groq__ApiKey (env var) - see .env.example. " +
                "Callers should check IsConfigured (or go through ILlmRouter) before calling this provider.");
        }

        var requestBody = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userPrompt }
            },
            ["temperature"] = 0.8,
            // Groq supports OpenAI-style JSON mode for its current Llama/GPT-OSS
            // models; verify against console.groq.com/docs/text-chat if you swap
            // GroqOptions.Model to something that might not support it.
            ["response_format"] = new JsonObject { ["type"] = "json_object" }
        };

        if (_options.MaxOutputTokens is { } maxTokens)
        {
            requestBody["max_tokens"] = maxTokens;
        }

        const int maxAttempts = 2;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/chat/completions")
                {
                    Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = $"Groq API returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(responseBody, 500)}";

                    // 429 = rate limit (RPM/RPD/TPM/TPD) hit; 402 = payment
                    // required (exhausted credits, used by some gateways) -
                    // both non-retryable-right-now, same treatment as Gemini's
                    // RESOURCE_EXHAUSTED. ILlmRouter falls back to the other
                    // configured provider on this instead of failing outright.
                    if ((int)response.StatusCode is 429 or 402)
                    {
                        throw new LlmQuotaExceededException(detail);
                    }

                    throw new HttpRequestException(detail);
                }

                return ExtractText(responseBody);
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Groq call failed (attempt {Attempt}/{MaxAttempts}), retrying", attempt, maxAttempts);
            }
        }

        throw new InvalidOperationException("Groq API call failed after retry.", lastError);
    }

    private static string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Groq API returned no choices.");
        }

        var first = choices[0];

        if (first.TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.GetString() is "content_filter")
        {
            throw new InvalidOperationException("Groq API blocked the response (finish_reason: content_filter).");
        }

        var text = first.GetProperty("message").GetProperty("content").GetString();
        return text ?? throw new InvalidOperationException("Groq API response had no message content.");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
