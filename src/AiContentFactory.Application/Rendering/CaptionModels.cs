namespace AiContentFactory.Application.Rendering;

public record CaptionWord(string Text, double StartSeconds, double EndSeconds);

/// <summary>
/// One on-screen group of words. <see cref="Words"/> carries per-word timing
/// for karaoke highlighting; renderers that don't karaoke just use
/// <see cref="Text"/> with the cue's own start/end.
///
/// Cues are produced by <see cref="ICaptionSegmentationService"/> from real
/// narration timing (<see cref="AiContentFactory.Application.Audio.AudioTiming"/>) -
/// the old proportional <c>CaptionTimeline</c> estimator has been retired.
/// </summary>
public record CaptionCue(double StartSeconds, double EndSeconds, IReadOnlyList<CaptionWord> Words)
{
    public string Text => string.Join(" ", Words.Select(w => w.Text));
}
