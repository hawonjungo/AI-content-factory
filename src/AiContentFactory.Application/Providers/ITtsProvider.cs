namespace AiContentFactory.Application.Providers;

/// <param name="Text">The narration to speak. This is the spoken text only - never the on-screen caption.</param>
/// <param name="VoiceName">Provider voice id (e.g. a Gemini prebuilt voice name). Null = provider default.</param>
/// <param name="StyleInstruction">Natural-language delivery direction ("read calmly, with long pauses"). Providers that can't steer style ignore it.</param>
/// <param name="SpeakingRate">Relative rate, 1.0 = normal (roughly 0.25-4.0). Null = provider default. Ignored where unsupported.</param>
/// <param name="Pitch">Relative pitch in semitones, 0 = normal. Null = provider default. Ignored where unsupported.</param>
/// <param name="Language">BCP-47 language/accent hint ("vi-VN", "en-US"). Null = let the provider infer.</param>
public record TtsRequest(
    string Text,
    string? VoiceName,
    string? StyleInstruction = null,
    double? SpeakingRate = null,
    double? Pitch = null,
    string? Language = null);

public record TtsResult(byte[] AudioBytes, string MimeType, double DurationSeconds, string Model);

/// <summary>
/// The replaceable seam for text-to-speech. Implementations map whatever of
/// <see cref="TtsRequest"/> they support onto their API and quietly ignore the
/// rest; callers should go through <c>ITtsService</c>, which adds voice-profile
/// resolution and post-generation audio validation on top.
/// </summary>
public interface ITtsProvider
{
    Task<TtsResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken = default);
}
