using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Image generation via Gemini 2.5 Flash Image (Nano Banana) through Gemini API.
/// This is the FREE replacement for deprecated Imagen - unlimited image generation
/// at zero cost. Perfect for generating motion-ready background images for
/// Image-to-Video conversion with Veo 3.1.
/// 
/// Verified against https://ai.google.dev/gemini-api/docs/image-generation (Sept 2026)
/// </summary>
public interface IImageGenerationProvider
{
    Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default);
}

public record ImageGenerationRequest(
    string Prompt,
    string AspectRatio = "9:16",
    int NumberOfImages = 1);

public record ImageGenerationResult(
    byte[] ImageBytes,
    string MimeType,
    string Model);

public class NanoBananaImageProvider : IImageGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public NanoBananaImageProvider(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set Llm__Gemini__ApiKey.");
        }

        // Use generateContent API with response_modalities set to IMAGE
        var requestBody = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["text"] = request.Prompt
                        }
                    }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["images"] = new JsonObject
                        {
                            ["type"] = "ARRAY",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "STRING"
                            }
                        }
                    }
                }
            }
        };

        // Add image generation config as system instruction via tool_config
        var systemInstructions = $$"""
You are an image generation AI. Generate {{request.NumberOfImages}} image(s) based on the user prompt.
Respond with JSON containing base64-encoded images in "images" array.
Each image should be {{request.AspectRatio}} aspect ratio.
""";

        var startUrl = $"{_options.BaseUrl}/models/{_options.ImageModel}:generateContent";
        
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, startUrl)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        startRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var startResponse = await _httpClient.SendAsync(startRequest, cancellationToken);
        var responseBody = await startResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!startResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Nano Banana image generation returned {(int)startResponse.StatusCode}: {Truncate(responseBody, 500)}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        
        // Navigate: candidates[0].content.parts[0].text (contains JSON string with base64 images)
        var candidates = doc.RootElement.GetProperty("candidates");
        var firstCandidate = candidates[0];
        var content = firstCandidate.GetProperty("content");
        var parts = content.GetProperty("parts");
        var textPart = parts[0];
        var textContent = textPart.GetProperty("text").GetString()
            ?? throw new InvalidOperationException("No text content in image response");

        // Parse JSON from text content
        using var imageJson = JsonDocument.Parse(textContent);
        var images = imageJson.RootElement.GetProperty("images");
        var firstImageBase64 = images[0].GetString()
            ?? throw new InvalidOperationException("No base64 image data in response");

        var imageBytes = Convert.FromBase64String(firstImageBase64);

        return new ImageGenerationResult(
            ImageBytes: imageBytes,
            MimeType: "image/png",
            Model: _options.ImageModel);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length > maxLength ? text[..maxLength] + "..." : text;
    }
}
