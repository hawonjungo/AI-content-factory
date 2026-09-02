using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppImageGenerationProvider = AiContentFactory.Application.Providers.IImageGenerationProvider;
using AppImageGenerationRequest = AiContentFactory.Application.Providers.ImageGenerationRequest;
using AppImageGenerationResult = AiContentFactory.Application.Providers.ImageGenerationResult;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Image generation via Gemini's "Nano Banana" model (gemini-2.5-flash-image)
/// through generateContent with responseModalities including IMAGE. Imagen
/// (the old dedicated image API) was deprecated and shut down Aug 17 2026 -
/// this is the current recommended path. Verify against
/// https://ai.google.dev/gemini-api/docs/image-generation before relying on
/// this in production.
/// </summary>
public class GeminiImageProvider : AppImageGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiImageProvider(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<AppImageGenerationResult> GenerateAsync(AppImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set Llm__Gemini__ApiKey.");
        }

        var promptText = request.Prompt;
        if (!string.IsNullOrWhiteSpace(request.NegativePrompt))
        {
            promptText += $"\n\nAvoid: {request.NegativePrompt}";
        }

        // Reference images go in as inlineData parts BEFORE the text, which is
        // how Gemini's image model does style transfer / "keep this character".
        var requestParts = new JsonArray();
        if (request.ReferenceImages is { Count: > 0 })
        {
            promptText = "Use the attached image(s) as a strict style and identity reference - keep the same character design, palette and mood.\n\n" + promptText;
            foreach (var reference in request.ReferenceImages)
            {
                requestParts.Add(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = reference.MimeType,
                        ["data"] = Convert.ToBase64String(reference.ImageBytes)
                    }
                });
            }
        }
        requestParts.Add(new JsonObject { ["text"] = promptText });

        var requestBody = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = requestParts
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray { "IMAGE" }
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.ImageModel}:generateContent?key={_options.ApiKey}";

        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini image generation returned {(int)response.StatusCode}: {Truncate(responseBody, 500)}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var parts = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("inlineData", out var inlineData))
            {
                var base64 = inlineData.GetProperty("data").GetString()
                    ?? throw new InvalidOperationException("Gemini image response had empty inlineData.");
                var mimeType = inlineData.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "image/png" : "image/png";
                return new AppImageGenerationResult(Convert.FromBase64String(base64), mimeType, _options.ImageModel);
            }
        }

        throw new InvalidOperationException("Gemini image response contained no image data - it may have returned text instead (check for a content policy refusal).");
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
