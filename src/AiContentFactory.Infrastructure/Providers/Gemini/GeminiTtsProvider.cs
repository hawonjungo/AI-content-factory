using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Text-to-speech via Gemini's generateContent with responseModalities:
/// ["AUDIO"]. Returns raw PCM (typically 16-bit mono, sample rate given in
/// the response mimeType, e.g. "audio/L16;codec=pcm;rate=24000") which this
/// wraps as a WAV file. Verify against
/// https://ai.google.dev/gemini-api/docs/generate-content/speech-generation
/// before relying on this in production - model name/response shape are
/// still evolving (the docs cover both a legacy generateContent path and a
/// newer Interactions API).
/// </summary>
public class GeminiTtsProvider : ITtsProvider
{
    private static readonly Regex SampleRatePattern = new(@"rate=(\d+)", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiTtsProvider(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<TtsResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured. Set Llm__Gemini__ApiKey.");
        }

        var voiceName = string.IsNullOrWhiteSpace(request.VoiceName) ? _options.TtsVoiceName : request.VoiceName;

        // Gemini's prebuilt-voice TTS path has no first-class rate/pitch knobs,
        // so style, rate, pitch and language are folded into a natural-language
        // delivery instruction prepended to the text - the documented way to
        // steer this model. A provider that DOES expose those knobs would map
        // the same TtsRequest fields onto its own parameters instead.
        var spokenText = BuildSpokenText(request);

        var requestBody = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = spokenText } }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray { "AUDIO" },
                ["speechConfig"] = new JsonObject
                {
                    ["voiceConfig"] = new JsonObject
                    {
                        ["prebuiltVoiceConfig"] = new JsonObject { ["voiceName"] = voiceName }
                    }
                }
            }
        };

        var url = $"{_options.BaseUrl}/models/{_options.TtsModel}:generateContent?key={_options.ApiKey}";

        using var content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini TTS returned {(int)response.StatusCode}: {Truncate(responseBody, 500)}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var inlineData = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("inlineData");

        var base64 = inlineData.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("Gemini TTS response had empty inlineData.");
        var mimeType = inlineData.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "audio/L16;rate=24000" : "audio/L16;rate=24000";

        var sampleRateMatch = SampleRatePattern.Match(mimeType);
        var sampleRate = sampleRateMatch.Success ? int.Parse(sampleRateMatch.Groups[1].Value) : 24000;

        var pcmBytes = Convert.FromBase64String(base64);
        var wavBytes = WavEncoder.WrapPcmAsWav(pcmBytes, sampleRate);
        var duration = WavEncoder.CalculateDurationSeconds(pcmBytes, sampleRate);

        return new TtsResult(wavBytes, "audio/wav", duration, _options.TtsModel);
    }

    private static string BuildSpokenText(TtsRequest request)
    {
        var directions = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.StyleInstruction))
        {
            directions.Add(request.StyleInstruction!.Trim().TrimEnd('.'));
        }

        if (request.SpeakingRate is { } rate && Math.Abs(rate - 1.0) > 0.01)
        {
            directions.Add(rate > 1.0 ? "at a faster pace" : "at a slower, more deliberate pace");
        }

        if (request.Pitch is { } pitch && Math.Abs(pitch) > 0.01)
        {
            directions.Add(pitch > 0 ? "in a slightly higher pitch" : "in a slightly lower pitch");
        }

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            directions.Add($"with a natural {request.Language!.Trim()} accent");
        }

        return directions.Count == 0
            ? request.Text
            : $"Read the following {string.Join(", ", directions)}:\n\n{request.Text}";
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
