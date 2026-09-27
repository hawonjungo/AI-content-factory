using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// <see cref="IVisionReviewProvider"/> on Gemini's generateContent with inline
/// images (same text model as <see cref="GeminiLlmProvider"/>). Exactly one
/// request per call - no retry - because it is billable. Thinking is turned
/// off and the output capped: the answer is a short JSON verdict, and
/// "thinking" tokens are billed as output.
/// </summary>
public class GeminiVisionReviewProvider : IVisionReviewProvider
{
    private const int MaxOutputTokens = 1024;

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiVisionReviewProvider> _logger;

    public GeminiVisionReviewProvider(HttpClient httpClient, IOptions<GeminiOptions> options, ILogger<GeminiVisionReviewProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<VisionReviewResult> ReviewAsync(VisionReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set Llm__Gemini__ApiKey (env var) - see .env.example.");
        }

        var parts = new JsonArray();
        foreach (var image in request.Images)
        {
            parts.Add(new JsonObject { ["text"] = image.Label });
            parts.Add(new JsonObject
            {
                ["inline_data"] = new JsonObject
                {
                    ["mime_type"] = image.MimeType,
                    ["data"] = Convert.ToBase64String(image.Bytes),
                },
            });
        }

        parts.Add(new JsonObject { ["text"] = request.Question });

        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.Instruction } } },
            ["contents"] = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = parts } },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["temperature"] = 0.2,
                ["maxOutputTokens"] = MaxOutputTokens,
                ["thinkingConfig"] = new JsonObject { ["thinkingBudget"] = 0 },
            },
        };

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The body can echo request details - log a bounded excerpt only, never the key/url.
            _logger.LogWarning("Gemini vision review failed with {Status}: {Body}", (int)response.StatusCode, Truncate(responseBody, 300));
            if ((int)response.StatusCode is 429 or 402 || responseBody.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase))
            {
                throw new LlmQuotaExceededException($"Gemini vision review hit a quota/spend limit ({(int)response.StatusCode}).");
            }

            throw new HttpRequestException($"Gemini vision review returned {(int)response.StatusCode}.");
        }

        return new VisionReviewResult(ExtractText(responseBody), _options.Model);
    }

    private static string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini vision review returned no usable answer.");
        }

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var reason) && reason.GetString() is "SAFETY" or "RECITATION" or "BLOCKLIST")
        {
            throw new InvalidOperationException($"Gemini vision review was blocked ({reason.GetString()}).");
        }

        if (!candidate.TryGetProperty("content", out var contentElement) ||
            !contentElement.TryGetProperty("parts", out var partsElement) ||
            partsElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Gemini vision review returned no text.");
        }

        return partsElement[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Gemini vision review returned no text.");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "...";
}
