namespace AiContentFactory.Application.Rendering;

/// <summary>
/// Tuning for <see cref="ICaptionSegmentationService"/>, bound from the
/// "Captions:Segmentation" config section. Defaults follow the short-form
/// caption norm: 3-6 words per cue, never 1-2, at most two lines.
/// </summary>
public class CaptionSegmentationOptions
{
    public const string SectionName = "Captions:Segmentation";

    /// <summary>Words a cue aims for before it will take a punctuation break.</summary>
    public int TargetWords { get; set; } = 4;

    /// <summary>A cue is never deliberately left shorter than this (trailing 1-2 word cues are merged away).</summary>
    public int MinWords { get; set; } = 3;

    /// <summary>Preferred upper bound; a cue is force-broken here even mid-phrase.</summary>
    public int MaxWords { get; set; } = 6;

    /// <summary>Absolute ceiling, only reached when merging a short tail back in.</summary>
    public int HardMaxWords { get; set; } = 7;

    /// <summary>Characters a cue may hold and still wrap within <see cref="MaxLines"/> lines.</summary>
    public int MaxCharsPerCue { get; set; } = 42;

    public int MaxLines { get; set; } = 2;

    public int MaxCharsPerLine => Math.Max(1, MaxCharsPerCue / Math.Max(1, MaxLines));
}
