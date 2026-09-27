using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;
using Xunit;

namespace AiContentFactory.Tests;

public class CaptionSegmentationServiceTests
{
    private readonly CaptionSegmentationService _service = new();
    private readonly AudioTimingService _timing = new();
    private readonly CaptionSegmentationOptions _options = new();

    private static CaptionSettings Captions(int maxWords = 6) => CaptionSettings.Create(
        enabled: true, fontFamily: "DejaVu Sans", fontSizePt: 72,
        primaryColor: "#FFFFFF", highlightColor: "#FFD400", outlineColor: "#000000",
        outlineWidth: 3, shadowDepth: 0, bold: true, uppercase: false,
        position: CaptionPosition.Bottom, marginVerticalPx: 220,
        maxWordsPerCue: maxWords, animation: CaptionAnimation.PopIn, karaoke: true);

    private IReadOnlyList<CaptionCue> Segment(string text, double seconds, double offset = 0, int maxWords = 6)
    {
        var timing = _timing.Compute(text, seconds);
        return _service.Segment(
            new[] { new SceneCaptionInput(text, timing, offset) },
            Captions(maxWords),
            _options);
    }

    [Fact]
    public void Captions_are_generated_from_scene_duration_when_there_is_no_narration_timing()
    {
        // "Keep original audio" clips reach here with AudioTiming.Empty. Captions
        // must NOT disappear - they spread across the scene's own 8s length.
        var text = "Five AI tools that quietly save you ten hours every single week.";
        var input = new SceneCaptionInput(text, AudioTiming.Empty, SceneOffsetSeconds: 4.0, SceneDurationSeconds: 8.0);

        var cues = _service.Segment(new[] { input }, Captions(), _options);

        Assert.NotEmpty(cues);
        Assert.All(cues, c => Assert.InRange(c.StartSeconds, 4.0 - 0.01, 12.0 + 0.01));
        Assert.All(cues, c => Assert.True(c.EndSeconds <= 12.01, $"cue ends at {c.EndSeconds}, past the 12s scene end"));
        Assert.True(cues.Max(c => c.EndSeconds) >= 10.0, "captions should span most of the 8s scene");
    }

    [Fact]
    public void A_scene_with_no_timing_and_no_duration_is_skipped_not_crashed()
    {
        var input = new SceneCaptionInput("some text", AudioTiming.Empty, 0, SceneDurationSeconds: 0);
        var cues = _service.Segment(new[] { input }, Captions(), _options);
        Assert.Empty(cues);
    }

    [Fact]
    public void The_subtitle_timeline_never_runs_past_the_visual_timeline()
    {
        // A short scene packed with words: cues must be clamped to the 4s end,
        // never overshoot it (that trips the final-video validator).
        var text = "One two three four five six seven eight nine ten eleven twelve thirteen fourteen.";
        var input = new SceneCaptionInput(text, AudioTiming.Empty, SceneOffsetSeconds: 0, SceneDurationSeconds: 4.0);

        var cues = _service.Segment(new[] { input }, Captions(), _options);

        Assert.NotEmpty(cues);
        Assert.All(cues, c => Assert.True(c.EndSeconds <= 4.0 + 1e-6, $"cue ends at {c.EndSeconds}, past the 4s scene"));
        Assert.All(cues, c => Assert.All(c.Words, w => Assert.True(w.EndSeconds <= 4.0 + 1e-6)));
    }

    [Fact]
    public void Does_not_split_blindly_on_periods()
    {
        // Naive text.Split('.') would make one-word cues "Wait" and "Stop".
        var cues = Segment("Wait. Stop. Look around you and breathe out slowly now.", 6.0);

        Assert.True(cues.Count >= 2);
        Assert.All(cues, c => Assert.True(c.Words.Count >= _options.MinWords,
            $"cue '{c.Text}' has {c.Words.Count} words"));
    }

    [Fact]
    public void Prefers_three_to_six_words_per_cue()
    {
        var text = "The stray tabby slipped through the open office door on a rainy monday morning " +
                   "and curled up on a warm laptop while nobody was watching it at all.";
        var cues = Segment(text, 12.0);

        Assert.All(cues, c => Assert.InRange(c.Words.Count, _options.MinWords, _options.HardMaxWords));
        Assert.True(cues.Average(c => c.Words.Count) <= 6);
        Assert.True(cues.Average(c => c.Words.Count) >= 3);
    }

    [Fact]
    public void No_cue_exceeds_the_hard_max_words()
    {
        var text = string.Join(' ', Enumerable.Range(1, 40).Select(i => $"word{i}"));
        var cues = Segment(text, 16.0);

        Assert.All(cues, c => Assert.True(c.Words.Count <= _options.HardMaxWords));
    }

    [Fact]
    public void Avoids_one_and_two_word_cues()
    {
        // 13 words: a blind 6+6+1 split would leave a 1-word tail.
        var text = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike";
        var cues = Segment(text, 9.0);

        Assert.DoesNotContain(cues, c => c.Words.Count < _options.MinWords);
    }

    [Fact]
    public void Every_cue_fits_within_two_lines()
    {
        var text = "This is a reasonably long stretch of narration that needs to be broken " +
                   "into several readable caption cues without any of them overflowing.";
        var cues = Segment(text, 11.0);

        Assert.All(cues, c => Assert.True(
            CaptionSegmentationService.EstimateLineCount(c.Text, _options.MaxCharsPerLine) <= _options.MaxLines,
            $"cue '{c.Text}' wraps to more than {_options.MaxLines} lines"));
    }

    [Fact]
    public void Punctuation_guides_but_does_not_control_the_split()
    {
        // No internal punctuation at all - still must not become one giant cue.
        var text = "the quick brown fox jumps over the lazy dog and then keeps running across the field";
        var cues = Segment(text, 10.0);

        Assert.True(cues.Count >= 3);
        Assert.All(cues, c => Assert.True(c.Words.Count <= _options.MaxWords));
    }

    [Fact]
    public void Caption_timing_aligns_with_the_narration_span()
    {
        var cues = Segment("The cat walked in and nobody noticed it for three whole days somehow.", seconds: 7.0, offset: 0);

        Assert.Equal(0, cues[0].StartSeconds, 1);
        Assert.InRange(cues[^1].EndSeconds, 6.0, 7.6);

        for (var i = 1; i < cues.Count; i++)
        {
            Assert.True(cues[i].StartSeconds >= cues[i - 1].EndSeconds - 0.051);
            Assert.True(cues[i].EndSeconds > cues[i].StartSeconds);
        }
    }

    [Fact]
    public void Scene_offset_shifts_all_cues_onto_the_timeline()
    {
        var cues = Segment("A short line of narration for the second scene here.", seconds: 5.0, offset: 20.0);

        Assert.All(cues, c => Assert.True(c.StartSeconds >= 20.0 - 0.001));
        Assert.All(cues, c => Assert.All(c.Words, w => Assert.True(w.StartSeconds >= 20.0 - 0.001)));
    }

    [Fact]
    public void Blank_caption_text_produces_no_cues()
    {
        var timing = _timing.Compute("spoken narration", 3.0);
        var cues = _service.Segment(
            new[] { new SceneCaptionInput("   ", timing, 0) },
            Captions(),
            _options);

        Assert.Empty(cues);
    }

    [Fact]
    public void Multiple_scenes_are_segmented_in_timeline_order()
    {
        var t1 = _timing.Compute("First scene narration line goes here now.", 4.0);
        var t2 = _timing.Compute("Second scene narration continues from there onward.", 4.0);

        var cues = _service.Segment(new[]
        {
            new SceneCaptionInput("First scene narration line goes here now.", t1, 0),
            new SceneCaptionInput("Second scene narration continues from there onward.", t2, 4.0),
        }, Captions(), _options);

        Assert.True(cues.First().StartSeconds < 4.0);
        Assert.Contains(cues, c => c.StartSeconds >= 4.0);
        Assert.True(cues.SequenceEqual(cues.OrderBy(c => c.StartSeconds)));
    }
}
