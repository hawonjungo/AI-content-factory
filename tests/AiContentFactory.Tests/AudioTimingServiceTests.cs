using AiContentFactory.Application.Audio;
using Xunit;

namespace AiContentFactory.Tests;

public class AudioTimingServiceTests
{
    private readonly AudioTimingService _service = new();

    [Fact]
    public void Total_duration_is_the_measured_audio_length_not_an_estimate()
    {
        var timing = _service.Compute("The cat sat quietly on the warm windowsill.", measuredAudioSeconds: 3.7);

        Assert.Equal(3.7, timing.TotalSeconds, 3);
        Assert.False(timing.FromProviderTimestamps);
        Assert.True(timing.HasTiming);
    }

    [Fact]
    public void Word_timings_span_the_whole_clip_and_are_monotonic()
    {
        var timing = _service.Compute("One two three four five six seven.", 5.0);

        Assert.Equal(0, timing.Words[0].StartSeconds, 3);
        Assert.Equal(5.0, timing.Words[^1].EndSeconds, 3);

        for (var i = 1; i < timing.Words.Count; i++)
        {
            Assert.True(timing.Words[i].StartSeconds >= timing.Words[i - 1].StartSeconds);
            Assert.True(timing.Words[i].EndSeconds > timing.Words[i].StartSeconds);
        }
    }

    [Fact]
    public void Sentence_boundaries_follow_terminal_punctuation()
    {
        var timing = _service.Compute("First sentence here. Second one now! And a third?", 6.0);

        Assert.Equal(3, timing.Sentences.Count);
        Assert.StartsWith("First", timing.Sentences[0].Text);
        Assert.True(timing.Sentences[1].StartSeconds >= timing.Sentences[0].EndSeconds - 0.001);
    }

    [Fact]
    public void End_of_sentence_words_hold_longer_than_mid_sentence_words()
    {
        var timing = _service.Compute("go go go go stop. run run run run run.", 8.0);

        var periodWord = timing.Words.First(w => w.Text.EndsWith("."));
        var plainWord = timing.Words.First(w => w.Text == "go");

        Assert.True(periodWord.DurationSeconds > plainWord.DurationSeconds);
    }

    [Fact]
    public void Provider_word_timestamps_are_used_verbatim_when_supplied()
    {
        var provided = new[]
        {
            new WordTiming("Hello", 0.0, 0.6),
            new WordTiming("there", 0.6, 1.4),
            new WordTiming("friend.", 1.4, 2.5),
        };

        var timing = _service.Compute("Hello there friend.", measuredAudioSeconds: 2.5, providerWordTimings: provided);

        Assert.True(timing.FromProviderTimestamps);
        Assert.Equal(0.6, timing.Words[0].EndSeconds, 3);
        Assert.Equal(1.4, timing.Words[1].EndSeconds, 3);
    }

    [Fact]
    public void Blank_text_or_zero_duration_yields_empty_timing()
    {
        Assert.False(_service.Compute("", 5).HasTiming);
        Assert.False(_service.Compute("some words", 0).HasTiming);
        Assert.Same(AudioTiming.Empty, _service.Compute("   ", 3));
    }
}
