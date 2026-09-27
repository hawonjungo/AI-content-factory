using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Tts;

/// <summary>
/// A concrete voice to synthesize with. Built from a <see cref="VoicePreset"/>
/// plus per-request overrides, so the same named preset can be spoken male or
/// female, faster or slower, or in another language without new presets.
/// </summary>
/// <param name="SpeakingRate">1.0 = normal. Clamped to 0.25-4.0 by the service. Ignored by providers that can't vary rate.</param>
/// <param name="Pitch">Semitones from default, 0 = normal. Clamped to -20..20. Ignored where unsupported.</param>
/// <param name="Language">BCP-47 hint ("vi-VN", "en-US"); null lets the provider infer from the text.</param>
public record VoiceProfile(
    string VoiceName,
    VoiceGender Gender,
    string? StyleInstruction,
    double? SpeakingRate,
    double? Pitch,
    string? Language)
{
    public static VoiceProfile FromPreset(
        VoicePreset preset,
        VoiceGender? gender = null,
        double? speakingRate = null,
        double? pitch = null,
        string? language = null) =>
        new(
            preset.GeminiVoiceName,
            gender ?? preset.Gender,
            preset.StyleInstruction,
            speakingRate,
            pitch,
            language);
}

/// <param name="NarrationText">Spoken text only. The on-screen caption is a separate concept (Scene.CaptionText) and is never sent here.</param>
public record TtsSynthesisRequest(string NarrationText, VoiceProfile Voice);

public record TtsSynthesisResult(
    byte[] AudioBytes,
    string MimeType,
    double DurationSeconds,
    string Model,
    AudioValidationResult Validation,
    bool IsFree = false);

/// <summary>
/// Wraps the replaceable <see cref="ITtsProvider"/> with the two things every
/// caller needs: voice-profile resolution (name / gender / style / rate /
/// pitch / language) and mandatory post-generation audio validation. A caller
/// that gets a result back is guaranteed non-empty, readable, non-silent
/// audio with a positive duration - otherwise this throws
/// <see cref="AudioValidationException"/>.
/// </summary>
public interface ITtsService
{
    Task<TtsSynthesisResult> SynthesizeAsync(TtsSynthesisRequest request, CancellationToken cancellationToken = default);
}

public class TtsService : ITtsService
{
    private const double MinRate = 0.25;
    private const double MaxRate = 4.0;
    private const double MinPitch = -20.0;
    private const double MaxPitch = 20.0;

    private readonly ITtsProvider _provider;
    private readonly IAudioValidator _validator;
    private readonly ILogger<TtsService> _logger;

    public TtsService(ITtsProvider provider, IAudioValidator validator, ILogger<TtsService> logger)
    {
        _provider = provider;
        _validator = validator;
        _logger = logger;
    }

    public async Task<TtsSynthesisResult> SynthesizeAsync(TtsSynthesisRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.NarrationText))
        {
            throw new ArgumentException("NarrationText is required for synthesis - an empty narration cannot produce audio.", nameof(request));
        }

        var voice = request.Voice;
        var rate = voice.SpeakingRate is { } r ? Math.Clamp(r, MinRate, MaxRate) : (double?)null;
        var pitch = voice.Pitch is { } p ? Math.Clamp(p, MinPitch, MaxPitch) : (double?)null;

        var result = await _provider.GenerateAsync(
            new TtsRequest(
                request.NarrationText,
                string.IsNullOrWhiteSpace(voice.VoiceName) ? null : voice.VoiceName,
                voice.StyleInstruction,
                rate,
                pitch,
                voice.Language),
            cancellationToken);

        var validation = _validator.Validate(result.AudioBytes, result.MimeType);
        if (!validation.IsValid)
        {
            _logger.LogError(
                "TTS produced invalid audio ({Bytes} bytes, model {Model}): {Error}",
                result.AudioBytes?.Length ?? 0, result.Model, validation.Error);
            throw new AudioValidationException(validation);
        }

        var duration = validation.DurationSeconds > 0 ? validation.DurationSeconds : result.DurationSeconds;
        if (duration <= 0)
        {
            throw new AudioValidationException(validation with { IsValid = false, Error = "audio duration could not be determined" });
        }

        return new TtsSynthesisResult(result.AudioBytes, result.MimeType, duration, result.Model, validation, result.IsFree);
    }
}
