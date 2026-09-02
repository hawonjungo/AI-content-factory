using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Image-to-Video generation via Veo 3.1 through Gemini API.
/// Converts a static image into an animated 5-8 second video clip with
/// configurable motion intensity (1-3 scale).
/// 
/// This is more efficient than text-to-video for the Google Flow strategy:
/// - Faster generation time
/// - Lower credit consumption (50 credits = 5 videos per day)
/// - Consistent visual output (image-anchored)
/// 
/// Verified against https://ai.google.dev/gemini-api/docs/veo (Sept 2026)
/// </summary>
public class VeoImageToVideoProvider : IVideoGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<VeoImageToVideoProvider> _logger;

    public VeoImageToVideoProvider(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<VeoImageToVideoProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generate a video from a still image using Veo 3.1 Image-to-Video.
    /// The request.ReferenceImages[0] is treated as the initial frame.
    /// The request.Prompt describes the motion/animation to apply.
    /// </summary>
    public async Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set Llm__Gemini__ApiKey.");
        }

        if (request.ReferenceImages?.Count == 0)
        {
            throw new InvalidOperationException(
                "Image-to-Video requires exactly one image in ReferenceImages[0]");
        }

        // Tier-selected model id from FlowGenerationService; configured default otherwise.
        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.VideoModel : request.Model.Trim();

        var imageBytes = request.ReferenceImages![0].ImageBytes;
        var imageBase64 = Convert.ToBase64String(imageBytes);

        // Build Veo 3.1 image-to-video request
        var instance = new JsonObject
        {
            ["image"] = new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = request.ReferenceImages[0].MimeType,
                    ["data"] = imageBase64
                }
            },
            ["prompt"] = request.Prompt
        };

        var parameters = new JsonObject
        {
            ["aspectRatio"] = request.AspectRatio,
            ["durationSeconds"] = request.DurationSeconds.ToString()
        };

        // Add motion-level directive if included in prompt
        if (!string.IsNullOrEmpty(request.NegativePrompt))
        {
            parameters["negativePrompt"] = request.NegativePrompt;
        }

        var requestBody = new JsonObject
        {
            ["instances"] = new JsonArray { instance },
            ["parameters"] = parameters
        };

        // Use veo-3.1-generate-preview (or veo-3.1-generate-lite for 50-credit constraint)
        var startUrl = $"{_options.BaseUrl}/models/{model}:predictLongRunning";
        
        _logger.LogInformation(
            "Starting Veo image-to-video generation: {Duration}s, aspect ratio {Ratio}",
            request.DurationSeconds,
            request.AspectRatio);

        using var startRequest = new HttpRequestMessage(HttpMethod.Post, startUrl)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        startRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var startResponse = await _httpClient.SendAsync(startRequest, cancellationToken);
        var startBody = await startResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!startResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Veo image-to-video start returned {(int)startResponse.StatusCode}: {Truncate(startBody, 500)}");
        }

        using var startDoc = JsonDocument.Parse(startBody);
        var operationName = startDoc.RootElement.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Veo start response had no operation name.");

        _logger.LogInformation("Veo operation started: {OperationName}", operationName);

        // Poll for completion
        var deadline = DateTime.UtcNow.AddSeconds(_options.VideoTimeoutSeconds);
        var delay = TimeSpan.FromSeconds(5);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Veo generation did not complete within {_options.VideoTimeoutSeconds}s");
            }

            await Task.Delay(delay, cancellationToken);

            var pollUrl = $"{_options.BaseUrl}/operations/{operationName}";
            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);
            pollRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var pollResponse = await _httpClient.SendAsync(pollRequest, cancellationToken);
            var pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!pollResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Veo poll returned {(int)pollResponse.StatusCode}: {Truncate(pollBody, 500)}");
            }

            using var pollDoc = JsonDocument.Parse(pollBody);
            var done = pollDoc.RootElement.GetProperty("done").GetBoolean();

            if (done)
            {
                _logger.LogInformation("Veo generation completed");
                var videoBytes = await GetVideoBytesFromOperationAsync(pollDoc, cancellationToken);
                return new VideoGenerationResult(videoBytes, "video/mp4", model);
            }

            _logger.LogDebug("Veo generation in progress, polling again in {Delay}s", delay.TotalSeconds);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.2, 30)); // Exponential backoff, max 30s
        }
    }

    private async Task<byte[]> GetVideoBytesFromOperationAsync(
        JsonDocument operationDoc,
        CancellationToken cancellationToken)
    {
        // Primary path: response.generated_videos[0].video.uri
        try
        {
            var response = operationDoc.RootElement.GetProperty("response");
            var generatedVideos = response.GetProperty("generatedVideos");
            var firstVideo = generatedVideos[0];
            var videoUri = firstVideo.GetProperty("video").GetProperty("uri").GetString();

            if (!string.IsNullOrEmpty(videoUri))
            {
                _logger.LogInformation("Downloading video from URI: {Uri}", Truncate(videoUri, 100));
                
                using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, videoUri);
                downloadRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

                using var downloadResponse = await _httpClient.SendAsync(downloadRequest, cancellationToken);
                downloadResponse.EnsureSuccessStatusCode();

                return await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary video URI path failed, trying fallback");
        }

        // Fallback: inline base64 bytes
        try
        {
            var response = operationDoc.RootElement.GetProperty("response");
            var generatedVideos = response.GetProperty("generatedVideos");
            var firstVideo = generatedVideos[0];
            
            if (firstVideo.TryGetProperty("videoBase64", out var base64Prop))
            {
                var base64 = base64Prop.GetString();
                if (!string.IsNullOrEmpty(base64))
                {
                    _logger.LogInformation("Using inline base64 video data");
                    return Convert.FromBase64String(base64);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fallback base64 path also failed");
        }

        throw new InvalidOperationException(
            "Could not locate video data in Veo response - neither URI nor inline bytes found");
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length > maxLength ? text[..maxLength] + "..." : text;
    }
}
