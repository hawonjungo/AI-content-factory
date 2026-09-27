using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Calls the Gemini Developer API's generateContent endpoint directly over
/// HTTP (no SDK dependency, to keep this adapter small and swappable).
/// Requests JSON-mode output (responseMimeType: application/json) since
/// every caller in this codebase parses the result as structured data.
///
/// Verify current pricing/model availability/commercial-use terms at
/// https://ai.google.dev/gemini-api/docs before relying on this in
/// production - these move independently of this codebase.
/// </summary>
public class GeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiLlmProvider> _logger;

    public GeminiLlmProvider(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiLlmProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set Llm__Gemini__ApiKey (env var) or ConnectionStrings equivalent - see .env.example.");
        }

        var requestBody = new JsonObject
        {
            ["system_instruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = userPrompt } }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["temperature"] = 0.8
            }
        };

        if (_options.MaxOutputTokens is { } maxTokens)
        {
            requestBody["generationConfig"]!["maxOutputTokens"] = maxTokens;
        }

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}";

        const int maxAttempts = 2;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(url, content, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var detail = $"Gemini API returned {(int)response.StatusCode} {response.StatusCode}: {Truncate(responseBody, 500)}";

                    // 429 (rate limit) / 402 (payment required - some proxies/
                    // gateways use this for exhausted credits) / RESOURCE_EXHAUSTED
                    // all mean a quota or billing spend cap was hit - a distinct,
                    // non-retryable condition. ILlmRouter treats this like any
                    // other failure and falls back to the configured secondary
                    // provider (e.g. Groq) instead of failing the request outright.
                    if ((int)response.StatusCode is 429 or 402 ||
                        responseBody.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
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
                _logger.LogWarning(ex, "Gemini call failed (attempt {Attempt}/{MaxAttempts}), retrying", attempt, maxAttempts);
            }
        }

        throw new InvalidOperationException("Gemini API call failed after retry.", lastError);
    }

    private static string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            var feedback = doc.RootElement.TryGetProperty("promptFeedback", out var pf) ? pf.ToString() : "no candidates returned";
            throw new InvalidOperationException($"Gemini API returned no usable candidates: {feedback}");
        }

        var firstCandidate = candidates[0];

        if (firstCandidate.TryGetProperty("finishReason", out var finishReason))
        {
            var reason = finishReason.GetString();

            if (reason is "SAFETY" or "RECITATION" or "BLOCKLIST")
            {
                throw new InvalidOperationException($"Gemini API blocked the response (finishReason: {reason}).");
            }

            // The model was cut off before finishing - almost always leaves
            // truncated/invalid JSON behind. Distinct exception so this is
            // diagnosable (see LlmResponseTruncatedException) rather than
            // surfacing as an opaque "invalid JSON" parse error further up.
            if (reason == "MAX_TOKENS")
            {
                throw new LlmResponseTruncatedException(
                    "Gemini API response was truncated (finishReason: MAX_TOKENS) - the configured MaxOutputTokens was reached before the model finished. Consider raising Llm:Gemini:MaxOutputTokens if this recurs.");
            }
        }

        var text = firstCandidate
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? throw new InvalidOperationException("Gemini API response had no text content.");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
