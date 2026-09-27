using System.Net.Http.Json;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Kokoro;

public class KokoroOptions
{
    public const string SectionName = "Tts:Kokoro";

    /// <summary>Base URL of a Kokoro-FastAPI server (e.g. http://kokoro:8880). Blank = the free voices are unavailable.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Free, self-hosted TTS: Kokoro-82M (Apache-2.0) served by Kokoro-FastAPI's
/// OpenAI-compatible <c>/v1/audio/speech</c> endpoint. Runs on the user's own
/// machine (docker compose profile "free-tts") - no API key, no per-call cost.
/// Kokoro has no Vietnamese voice: a non-English request is refused with a
/// clear message instead of producing unintelligible audio.
/// </summary>
public class KokoroTtsProvider : ITtsProvider
{
    /// <summary>Voice ids routed here carry this prefix (see the Kokoro voice presets).</summary>
    public const string VoicePrefix = "kokoro:";
    public const string ModelName = "kokoro-82m (local)";

    private readonly HttpClient _httpClient;
    private readonly KokoroOptions _options;
    private readonly ILogger<KokoroTtsProvider> _logger;

    public KokoroTtsProvider(HttpClient httpClient, IOptions<KokoroOptions> options, ILogger<KokoroTtsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TtsResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new InvalidOperationException(
                "Giọng đọc miễn phí (Kokoro) chưa được bật. Chạy `docker compose --profile free-tts up -d kokoro` rồi đặt Tts__Kokoro__BaseUrl.");
        }

        if (!string.IsNullOrWhiteSpace(request.Language) && !request.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Giọng đọc miễn phí (Kokoro) chỉ đọc được tiếng Anh, còn dự án đang đọc '{request.Language}'. Hãy chọn một giọng Gemini.");
        }

        var voice = (request.VoiceName ?? string.Empty).StartsWith(VoicePrefix, StringComparison.OrdinalIgnoreCase)
            ? request.VoiceName![VoicePrefix.Length..]
            : "af_heart";

        var body = new
        {
            model = "kokoro",
            input = request.Text,
            voice,
            response_format = "wav",
            speed = Math.Clamp(request.SpeakingRate ?? 1.0, 0.5, 2.0),
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync($"{_options.BaseUrl.TrimEnd('/')}/v1/audio/speech", body, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Never fall back to a paid voice silently - the user chose a free one.
            _logger.LogWarning(ex, "Kokoro TTS server at {BaseUrl} is unreachable", _options.BaseUrl);
            throw new InvalidOperationException(
                "Không kết nối được máy chủ giọng đọc miễn phí (Kokoro). Kiểm tra container 'kokoro' đang chạy (docker compose --profile free-tts up -d kokoro).",
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Kokoro TTS returned {Status}: {Detail}", (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail);
                throw new InvalidOperationException($"Máy chủ giọng đọc miễn phí (Kokoro) báo lỗi {(int)response.StatusCode}.");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            // Duration 0 = let the audio validator measure it from the WAV header.
            return new TtsResult(bytes, "audio/wav", 0, ModelName, IsFree: true);
        }
    }
}
