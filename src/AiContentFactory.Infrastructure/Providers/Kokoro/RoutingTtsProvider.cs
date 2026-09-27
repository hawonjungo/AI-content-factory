using AiContentFactory.Application.Providers;
using AiContentFactory.Infrastructure.Providers.Gemini;

namespace AiContentFactory.Infrastructure.Providers.Kokoro;

/// <summary>
/// The app's single <see cref="ITtsProvider"/>: a voice id starting with
/// <see cref="KokoroTtsProvider.VoicePrefix"/> goes to the free local Kokoro
/// server, everything else to Gemini TTS exactly as before. There is no
/// fallback between them - a failing free voice never turns into a paid call.
/// </summary>
public class RoutingTtsProvider : ITtsProvider
{
    private readonly GeminiTtsProvider _gemini;
    private readonly KokoroTtsProvider _kokoro;

    public RoutingTtsProvider(GeminiTtsProvider gemini, KokoroTtsProvider kokoro)
    {
        _gemini = gemini;
        _kokoro = kokoro;
    }

    public static bool IsKokoroVoice(string? voiceName) =>
        voiceName is not null && voiceName.StartsWith(KokoroTtsProvider.VoicePrefix, StringComparison.OrdinalIgnoreCase);

    public Task<TtsResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken = default) =>
        IsKokoroVoice(request.VoiceName)
            ? _kokoro.GenerateAsync(request, cancellationToken)
            : _gemini.GenerateAsync(request, cancellationToken);
}
