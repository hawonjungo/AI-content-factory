using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Video generation via Veo 3.1 through the Gemini API's asynchronous
/// predictLongRunning endpoint + operation polling, with reference images
/// for character/style consistency across clips. Verified against
/// https://ai.google.dev/gemini-api/docs/veo (Aug 2026), including the
/// specific field names used here:
///   - request: instances[].referenceImages[] = {image: {inlineData: {mimeType, data}}, referenceType: "asset"}
///   - request: instances[].parameters.aspectRatio
///   - completed operation: response.generateVideoResponse.generatedSamples[0].video.uri,
///     downloaded via GET with the same x-goog-api-key header.
/// The defensive fallback search in GetVideoBytesFromOperationAsync exists
/// for the case where a given account/tier returns a slightly different
/// shape (e.g. inline bytes instead of a URI) - the primary path above is
/// tried first since it's the one actually confirmed against current docs.
/// </summary>
public class VeoVideoProvider : IVideoGenerationProvider
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public VeoVideoProvider(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set Llm__Gemini__ApiKey.");
        }

        // The caller (FlowGenerationService) picks the model per tier; fall back
        // to the configured default when it doesn't.
        var model = string.IsNullOrWhiteSpace(request.Model) ? _options.VideoModel : request.Model.Trim();

        var instance = new JsonObject { ["prompt"] = request.Prompt };

        // The standard pipeline deliberately uses image-to-video. The scene
        // anchor is created from the approved character + environment images,
        // then supplied as Veo's first frame for every clip.
        if (request.InitialImage is not null)
        {
            instance["image"] = new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = request.InitialImage.MimeType,
                    ["data"] = Convert.ToBase64String(request.InitialImage.ImageBytes)
                }
            };
        }

        if (request.InitialImage is null && request.ReferenceImages is { Count: > 0 })
        {
            var referenceImagesArray = new JsonArray();
            foreach (var reference in request.ReferenceImages.Take(3)) // Veo supports up to 3
            {
                referenceImagesArray.Add(new JsonObject
                {
                    ["image"] = new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = reference.MimeType,
                            ["data"] = Convert.ToBase64String(reference.ImageBytes)
                        }
                    },
                    ["referenceType"] = "asset"
                });
            }
            instance["referenceImages"] = referenceImagesArray;
        }

        var parameters = new JsonObject { ["aspectRatio"] = request.AspectRatio };
        if (request.InitialImage is not null)
        {
            // Veo image-to-video accepts 4, 6 or 8 second source clips. The
            // renderer already loops a clip when narration runs longer, so an
            // 8-second anchor is the safe choice for any custom plan length.
            parameters["durationSeconds"] = request.DurationSeconds switch
            {
                4 => "4",
                6 => "6",
                _ => "8"
            };
        }
        if (!string.IsNullOrWhiteSpace(request.NegativePrompt))
        {
            parameters["negativePrompt"] = request.NegativePrompt;
        }

        var requestBody = new JsonObject
        {
            ["instances"] = new JsonArray { instance },
            ["parameters"] = parameters
        };

        var startResult = await StartOperationAsync(model, requestBody, cancellationToken);

        // veo-3.1-fast-generate-preview is text-to-video ONLY - it rejects any
        // inline image, whether that's an image-to-video first frame or
        // reference images. A rejected request is not billed, so strip every
        // image input and retry as pure text-to-video rather than failing the
        // whole project. The prompt already carries the consistency
        // instructions (see SceneAssetGenerator.BuildClipPrompt), and the
        // scene-anchor image is still generated and kept as the still.
        if (!startResult.IsSuccessStatusCode &&
            (instance.ContainsKey("image") || instance.ContainsKey("referenceImages")) &&
            startResult.Body.Contains("`inlineData` isn't supported by this model", StringComparison.OrdinalIgnoreCase))
        {
            instance.Remove("image");
            instance.Remove("referenceImages");
            parameters.Remove("durationSeconds"); // was only valid for the image-to-video path
            startResult = await StartOperationAsync(model, requestBody, cancellationToken);
        }

        if (!startResult.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Veo generation start returned {startResult.StatusCode}: {Truncate(startResult.Body, 500)}");
        }

        using var startDoc = JsonDocument.Parse(startResult.Body);
        var operationName = startDoc.RootElement.GetProperty("name").GetString()
            ?? throw new InvalidOperationException("Veo start response had no operation name.");

        var deadline = DateTime.UtcNow.AddSeconds(_options.VideoTimeoutSeconds);
        var delay = TimeSpan.FromSeconds(5);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pollUrl = $"{_options.BaseUrl}/{operationName}";
            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);
            pollRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var pollResponse = await _httpClient.SendAsync(pollRequest, cancellationToken);
            var pollBody = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!pollResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Veo operation poll returned {(int)pollResponse.StatusCode}: {Truncate(pollBody, 500)}");
            }

            using var pollDoc = JsonDocument.Parse(pollBody);
            var isDone = pollDoc.RootElement.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean();

            if (isDone)
            {
                if (pollDoc.RootElement.TryGetProperty("error", out var errorEl))
                {
                    throw new InvalidOperationException($"Veo generation failed: {errorEl}");
                }

                var videoBytes = await GetVideoBytesFromOperationAsync(pollDoc.RootElement, cancellationToken);
                return new VideoGenerationResult(videoBytes, "video/mp4", model);
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Veo generation did not complete within {_options.VideoTimeoutSeconds}s.");
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 20));
        }
    }

    private async Task<(bool IsSuccessStatusCode, int StatusCode, string Body)> StartOperationAsync(
        string model,
        JsonObject requestBody,
        CancellationToken cancellationToken)
    {
        var startUrl = $"{_options.BaseUrl}/models/{model}:predictLongRunning";
        using var startRequest = new HttpRequestMessage(HttpMethod.Post, startUrl)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json")
        };
        startRequest.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var startResponse = await _httpClient.SendAsync(startRequest, cancellationToken);
        var body = await startResponse.Content.ReadAsStringAsync(cancellationToken);
        return (startResponse.IsSuccessStatusCode, (int)startResponse.StatusCode, body);
    }

    private async Task<byte[]> GetVideoBytesFromOperationAsync(JsonElement operationRoot, CancellationToken cancellationToken)
    {
        if (!operationRoot.TryGetProperty("response", out var response))
        {
            throw new InvalidOperationException("Veo operation completed but had no 'response' field.");
        }

        // Primary path, confirmed against current docs: response.generateVideoResponse.generatedSamples[0].video.uri
        if (response.TryGetProperty("generateVideoResponse", out var genResponse) &&
            genResponse.TryGetProperty("generatedSamples", out var samples) &&
            samples.ValueKind == JsonValueKind.Array && samples.GetArrayLength() > 0)
        {
            var firstSample = samples[0];
            if (firstSample.TryGetProperty("video", out var video) && video.TryGetProperty("uri", out var uriEl))
            {
                var uri = uriEl.GetString();
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    return await DownloadVideoAsync(uri, cancellationToken);
                }
            }
        }

        // Defensive fallback for a differently-shaped response (e.g. a
        // different account/tier returning inline bytes instead of a URI).
        if (TryFindStringField(response, "bytesBase64Encoded", out var base64) ||
            TryFindStringField(response, "videoBytes", out base64))
        {
            return Convert.FromBase64String(base64);
        }

        if (TryFindStringField(response, "uri", out var fallbackUri) ||
            TryFindStringField(response, "gcsUri", out fallbackUri))
        {
            return await DownloadVideoAsync(fallbackUri, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Could not locate video data in Veo operation response. Raw response: {Truncate(response.ToString(), 1000)}");
    }

    private async Task<byte[]> DownloadVideoAsync(string uri, CancellationToken cancellationToken)
    {
        using var downloadRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        downloadRequest.Headers.Add("x-goog-api-key", _options.ApiKey);
        using var downloadResponse = await _httpClient.SendAsync(downloadRequest, cancellationToken);
        downloadResponse.EnsureSuccessStatusCode();
        return await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static bool TryFindStringField(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    value = property.Value.GetString() ?? string.Empty;
                    return true;
                }

                if (TryFindStringField(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindStringField(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
