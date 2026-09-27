using System.Globalization;
using System.Text.RegularExpressions;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Infrastructure.Rendering;
using Xunit;

namespace AiContentFactory.Tests;

public class AssSubtitleWriterTests
{
    private static CaptionSettings Karaoke() => CaptionSettings.Create(
        enabled: true, fontFamily: "DejaVu Sans", fontSizePt: 62,
        primaryColor: "#FFFFFF", highlightColor: "#FFD400", outlineColor: "#000000",
        outlineWidth: 4, shadowDepth: 0, bold: true, uppercase: true,
        position: CaptionPosition.Bottom, marginVerticalPx: 260,
        maxWordsPerCue: 3, animation: CaptionAnimation.PopIn, karaoke: true);

    private static string Write(IReadOnlyList<CaptionCue> cues, CaptionSettings settings)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ass-{Guid.NewGuid():N}.ass");
        try
        {
            AssSubtitleWriter.Write(cues, settings, 1080, 1920, path);
            return File.ReadAllText(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IEnumerable<(double Start, double End)> DialogueWindows(string ass)
    {
        foreach (Match m in Regex.Matches(ass, @"Dialogue: 0,(\d):(\d\d):(\d\d\.\d\d),(\d):(\d\d):(\d\d\.\d\d),"))
        {
            double T(int a, int b, int c) => int.Parse(m.Groups[a].Value) * 3600
                + int.Parse(m.Groups[b].Value) * 60
                + double.Parse(m.Groups[c].Value, CultureInfo.InvariantCulture);
            yield return (T(1, 2, 3), T(4, 5, 6));
        }
    }

    [Fact]
    public void Wrapping_is_enabled_so_wide_cues_break_instead_of_overflowing()
    {
        var ass = Write(new[] { new CaptionCue(0, 2, new[] { new CaptionWord("word", 0, 2) }) }, Karaoke());
        Assert.Contains("WrapStyle: 0", ass);
        Assert.DoesNotContain("WrapStyle: 2", ass);
    }

    [Fact]
    public void Karaoke_word_windows_are_never_shorter_than_the_readable_minimum()
    {
        // 3 words estimated at ~0.05s each inside a 1.2s cue - the raw slices
        // would be unreadable; every rendered window must be >= ~0.15s.
        var words = new[]
        {
            new CaptionWord("ALPHA", 0.00, 0.05),
            new CaptionWord("BETA", 0.05, 0.10),
            new CaptionWord("GAMMA", 0.10, 1.20),
        };
        var ass = Write(new[] { new CaptionCue(0, 1.2, words) }, Karaoke());

        var windows = DialogueWindows(ass).ToList();
        Assert.Equal(3, windows.Count);
        Assert.All(windows, w => Assert.True(w.End - w.Start >= 0.149, $"window {w.Start}-{w.End} too short"));
        Assert.Equal(1.2, windows[^1].End, 2); // last word runs to the cue end
    }

    [Fact]
    public void PopIn_transition_fits_inside_a_short_first_window()
    {
        var words = new[]
        {
            new CaptionWord("A", 0.00, 0.12),
            new CaptionWord("B", 0.12, 0.24),
            new CaptionWord("C", 0.24, 0.40),
        };
        var ass = Write(new[] { new CaptionCue(0, 0.40, words) }, Karaoke());

        // \t(0,<ms>,\fscx100...) - ms must be small enough to complete in-window,
        // never the old fixed 110 when the window is only ~0.15s.
        var m = Regex.Match(ass, @"\\t\(0,(\d+),\\fscx100");
        Assert.True(m.Success, "no PopIn transition emitted");
        Assert.InRange(int.Parse(m.Groups[1].Value), 40, 110);
    }

    [Fact]
    public void Windows_stay_within_the_cue_and_do_not_overlap()
    {
        var words = new[]
        {
            new CaptionWord("ONE", 0.0, 0.3),
            new CaptionWord("TWO", 0.3, 0.6),
            new CaptionWord("THREE", 0.6, 0.9),
        };
        var ass = Write(new[] { new CaptionCue(2.0, 2.9, words.Select(w => new CaptionWord(w.Text, w.StartSeconds + 2.0, w.EndSeconds + 2.0)).ToList()) }, Karaoke());

        var windows = DialogueWindows(ass).ToList();
        Assert.All(windows, w => Assert.InRange(w.Start, 2.0 - 1e-6, 2.9 + 1e-6));
        Assert.All(windows, w => Assert.True(w.End <= 2.9 + 1e-6));
        for (var i = 1; i < windows.Count; i++)
        {
            Assert.True(windows[i].Start >= windows[i - 1].Start, "windows must be ordered");
        }
    }
}
