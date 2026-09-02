using AiContentFactory.Application.Audio;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class AudioValidatorTests
{
    private readonly AudioValidator _validator = new();

    [Fact]
    public void A_normal_wav_is_valid_with_a_positive_duration()
    {
        var result = _validator.Validate(WavTestData.Pcm16(24000, seconds: 2.0, amplitude: 6000), "audio/wav");

        Assert.True(result.IsValid);
        Assert.False(result.IsSilent);
        Assert.Equal(24000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.InRange(result.DurationSeconds, 1.9, 2.1);
    }

    [Fact]
    public void An_empty_buffer_is_invalid()
    {
        var result = _validator.Validate(WavTestData.Empty());

        Assert.False(result.IsValid);
        Assert.Equal(0, result.DurationSeconds);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void A_null_buffer_is_invalid()
    {
        var result = _validator.Validate(null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_buffer_too_short_to_be_a_wav_is_invalid()
    {
        var result = _validator.Validate(WavTestData.TooShort());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_non_riff_buffer_is_invalid()
    {
        var result = _validator.Validate(WavTestData.NotRiff());

        Assert.False(result.IsValid);
        Assert.Contains("RIFF", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_wav_with_an_empty_data_chunk_has_zero_duration_and_is_invalid()
    {
        var result = _validator.Validate(WavTestData.EmptyData());

        Assert.False(result.IsValid);
        Assert.Equal(0, result.DurationSeconds);
    }

    [Fact]
    public void Digital_silence_is_detected_and_rejected()
    {
        var result = _validator.Validate(WavTestData.Silent(24000, seconds: 1.5));

        Assert.False(result.IsValid);
        Assert.True(result.IsSilent);
        // The header still parsed, so duration is known even though it's unusable.
        Assert.InRange(result.DurationSeconds, 1.4, 1.6);
    }

    [Fact]
    public void A_barely_audible_clip_above_the_floor_passes()
    {
        var result = _validator.Validate(WavTestData.Pcm16(24000, 1.0, amplitude: 64));

        Assert.True(result.IsValid);
        Assert.False(result.IsSilent);
    }
}
