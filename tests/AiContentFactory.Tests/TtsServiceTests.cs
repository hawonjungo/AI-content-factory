using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Presets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Tts;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiContentFactory.Tests;

public class TtsServiceTests
{
    private static (TtsService Service, FakeTtsProvider Provider) Build()
    {
        var provider = new FakeTtsProvider();
        var service = new TtsService(provider, new AudioValidator(), NullLogger<TtsService>.Instance);
        return (service, provider);
    }

    private static VoiceProfile Profile(VoiceGender gender = VoiceGender.Unspecified, double? rate = null, string? language = null) =>
        VoiceProfile.FromPreset(PresetCatalog.ResolveVoice("narrator-deep", gender), gender, rate, pitch: null, language);

    [Fact]
    public async Task Valid_audio_comes_back_with_a_measured_duration()
    {
        var (service, _) = Build();

        var result = await service.SynthesizeAsync(new TtsSynthesisRequest("Hello world.", Profile()));

        Assert.True(result.Validation.IsValid);
        Assert.True(result.DurationSeconds > 0);
        Assert.Equal("fake-tts", result.Model);
    }

    [Fact]
    public async Task Silent_audio_from_the_provider_fails_the_generation()
    {
        var (service, provider) = Build();
        provider.Responder = _ => new TtsResult(WavTestData.Silent(), "audio/wav", 1.0, "fake-tts");

        await Assert.ThrowsAsync<AudioValidationException>(
            () => service.SynthesizeAsync(new TtsSynthesisRequest("Hello world.", Profile())));
    }

    [Fact]
    public async Task Empty_audio_from_the_provider_fails_the_generation()
    {
        var (service, provider) = Build();
        provider.Responder = _ => new TtsResult(Array.Empty<byte>(), "audio/wav", 0, "fake-tts");

        await Assert.ThrowsAsync<AudioValidationException>(
            () => service.SynthesizeAsync(new TtsSynthesisRequest("Hello world.", Profile())));
    }

    [Fact]
    public async Task Provider_reporting_zero_duration_is_repaired_from_the_decoded_audio()
    {
        var (service, provider) = Build();
        provider.Responder = _ => new TtsResult(WavTestData.Pcm16(24000, 3.0, 5000), "audio/wav", DurationSeconds: 0, "fake-tts");

        var result = await service.SynthesizeAsync(new TtsSynthesisRequest("A longer line of narration.", Profile()));

        Assert.InRange(result.DurationSeconds, 2.9, 3.1);
    }

    [Fact]
    public async Task Blank_narration_is_rejected_before_any_provider_call()
    {
        var (service, provider) = Build();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SynthesizeAsync(new TtsSynthesisRequest("   ", Profile())));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Male_and_female_selections_send_different_voice_names_to_the_provider()
    {
        var (service, provider) = Build();

        await service.SynthesizeAsync(new TtsSynthesisRequest("Line.", Profile(VoiceGender.Male)));
        var maleVoice = provider.LastRequest!.VoiceName;

        await service.SynthesizeAsync(new TtsSynthesisRequest("Line.", Profile(VoiceGender.Female)));
        var femaleVoice = provider.LastRequest!.VoiceName;

        Assert.False(string.IsNullOrWhiteSpace(maleVoice));
        Assert.False(string.IsNullOrWhiteSpace(femaleVoice));
        Assert.NotEqual(maleVoice, femaleVoice);
        Assert.Equal(PresetCatalog.FindVoiceByGender(VoiceGender.Male)!.GeminiVoiceName, maleVoice);
        Assert.Equal(PresetCatalog.FindVoiceByGender(VoiceGender.Female)!.GeminiVoiceName, femaleVoice);
    }

    [Fact]
    public async Task Speaking_rate_is_clamped_and_language_is_forwarded()
    {
        var (service, provider) = Build();

        await service.SynthesizeAsync(new TtsSynthesisRequest("Line.", Profile(rate: 99.0, language: "vi-VN")));

        Assert.Equal(4.0, provider.LastRequest!.SpeakingRate); // clamped to MaxRate
        Assert.Equal("vi-VN", provider.LastRequest!.Language);
    }
}
