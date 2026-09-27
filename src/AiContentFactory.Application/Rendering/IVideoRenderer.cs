using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.Rendering;

/// <param name="IsStillImage">
/// True when VisualAbsolutePath points at an image, not a video - the renderer
/// then holds it for the scene's duration with a camera move (see
/// <paramref name="Motion"/>) instead of looping a clip.
/// </param>
/// <param name="Motion">Camera move for an image scene; ignored for a video clip.</param>
/// <param name="TransitionIn">Applied at the start of the segment (fade in) so scenes don't hard-cut.</param>
/// <param name="AudioSource">
/// Which audio the segment carries: the mapped voice track (default), the clip's
/// own embedded audio, or forced silence. Set by the Step 6 Voice option.
/// </param>
public record RenderScene(
    string VisualAbsolutePath,
    string? VoiceAbsolutePath,
    double DurationSeconds,
    string Narration,
    bool IsStillImage = false,
    SceneMotion Motion = SceneMotion.None,
    TransitionKind TransitionIn = TransitionKind.None,
    SceneAudioSource AudioSource = SceneAudioSource.Voice);

/// <param name="Captions">
/// Full caption styling from the project's caption preset (plus any manual
/// edits). Replaces the old bool + hardcoded force_style: captions are now a
/// preset with font, colour, position, word grouping, and animation, and
/// <see cref="CaptionSettings.Enabled"/> is what "captions off" means.
/// </param>
/// <param name="Cues">
/// Pre-timed cues from <see cref="ICaptionSegmentationService"/>, aligned to
/// the real narration timing. Built in the Application layer so the timing
/// rules stay testable and provider-independent.
/// </param>
/// <param name="AudioMix">
/// Full audio mix (narration bed + ducked music + ambience + SFX + fades).
/// When null the renderer falls back to the legacy "mix a single background
/// music track at a fixed low volume" behaviour using
/// <paramref name="BackgroundMusicAbsolutePath"/>.
/// </param>
public record RenderRequest(
    IReadOnlyList<RenderScene> Scenes,
    string? BackgroundMusicAbsolutePath,
    string OutputAbsolutePath,
    CaptionSettings Captions,
    IReadOnlyList<CaptionCue> Cues,
    int Width = 1080,
    int Height = 1920,
    int Fps = 30,
    AudioMixSpec? AudioMix = null);

public record RenderResult(string OutputAbsolutePath, double DurationSeconds);

/// <param name="BackgroundAbsolutePath">A clip or image to draw the sample caption over.</param>
/// <param name="SampleText">The words to draw - usually the project's first cue.</param>
public record CaptionPreviewRequest(
    string BackgroundAbsolutePath,
    string SampleText,
    CaptionSettings Captions,
    string OutputAbsolutePath,
    int Width = 1080,
    int Height = 1920);

/// <summary>
/// Renders a storyboard's generated Veo clips into one finished vertical
/// MP4: scale/pad each clip to the target aspect ratio, mux in its
/// voice-over, concatenate clips in order, burn in styled captions, and mix
/// in background music if provided.
/// </summary>
public interface IVideoRenderer
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Draws one caption cue over a single frame and returns a PNG. Lets the
    /// caption editor show what a preset actually looks like without spending
    /// a full render (and, more importantly, without regenerating any clips).
    /// </summary>
    Task<string> RenderCaptionPreviewAsync(CaptionPreviewRequest request, CancellationToken cancellationToken = default);
}
