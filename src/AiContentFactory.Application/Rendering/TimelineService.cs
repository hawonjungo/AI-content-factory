using AiContentFactory.Application.Audio;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Rendering;

public class TimelineOptions
{
    public const string SectionName = "Timeline";

    /// <summary>Length of a scene that has no narration - just enough to register, never padded with filler.</summary>
    public double NoNarrationSceneSeconds { get; set; } = 2.5;

    public double MinSceneSeconds { get; set; } = 1.2;

    /// <summary>Upper clamp for a single scene; a real narration this long is unusual but allowed.</summary>
    public double MaxSceneSeconds { get; set; } = 22;

    public double TransitionSeconds { get; set; } = 0.25;

    public double TargetMinTotalSeconds { get; set; } = 55;
    public double TargetMaxTotalSeconds { get; set; } = 75;
}

/// <param name="NarrationTiming">Actual narration timing for the scene, or <see cref="AudioTiming.Empty"/> for a silent scene.</param>
/// <param name="CaptionText">Scene.EffectiveCaptionText - the on-screen text, distinct from the narration.</param>
/// <param name="AudioSource">Which audio the scene segment uses (Step 6 Voice option). Defaults to the TTS voice track.</param>
/// <param name="DurationSecondsOverride">
/// When set, drives the scene length directly (still clamped) instead of the
/// narration timing - used by the "keep original audio" / "mute" modes so a
/// scene lasts exactly as long as its clip.
/// </param>
public record TimelineSceneInput(
    int SceneNumber,
    string VisualAbsolutePath,
    bool IsStillImage,
    string? VoiceAbsolutePath,
    AudioTiming NarrationTiming,
    string CaptionText,
    SceneAudioSource AudioSource = SceneAudioSource.Voice,
    double? DurationSecondsOverride = null);

public record TimelineBuildRequest(
    IReadOnlyList<TimelineSceneInput> Scenes,
    CaptionSettings Captions,
    string? MusicPath = null,
    IReadOnlyList<SfxCue>? Sfx = null,
    string? AmbiencePath = null);

public interface ITimelineService
{
    /// <summary>
    /// Assembles the timeline with narration as the authority: each scene lasts
    /// exactly as long as its narration audio (clamped), captions are segmented
    /// and aligned to that audio, image scenes get a camera move, and the audio
    /// mix (music ducking, ambience, SFX, fades) is resolved.
    /// </summary>
    Timeline Build(TimelineBuildRequest request);
}

public class TimelineService : ITimelineService
{
    // Image scenes cycle through moves so a run of stills doesn't feel like a slideshow.
    private static readonly SceneMotion[] MotionCycle =
    {
        SceneMotion.KenBurnsIn,
        SceneMotion.PanRight,
        SceneMotion.KenBurnsOut,
        SceneMotion.PanLeft,
        SceneMotion.Parallax
    };

    private readonly ICaptionSegmentationService _captionSegmentation;
    private readonly IAudioMixingService _audioMixing;
    private readonly TimelineOptions _options;
    private readonly CaptionSegmentationOptions _captionOptions;

    public TimelineService(
        ICaptionSegmentationService captionSegmentation,
        IAudioMixingService audioMixing,
        IOptions<TimelineOptions> options,
        IOptions<CaptionSegmentationOptions> captionOptions)
    {
        _captionSegmentation = captionSegmentation;
        _audioMixing = audioMixing;
        _options = options.Value;
        _captionOptions = captionOptions.Value;
    }

    public Timeline Build(TimelineBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var offsets = new double[request.Scenes.Count];
        var durations = new double[request.Scenes.Count];
        var cursor = 0.0;
        var stillIndex = 0;

        for (var i = 0; i < request.Scenes.Count; i++)
        {
            var scene = request.Scenes[i];
            var duration = scene.DurationSecondsOverride
                ?? (scene.NarrationTiming.HasTiming
                    ? scene.NarrationTiming.TotalSeconds
                    : _options.NoNarrationSceneSeconds);
            duration = Math.Clamp(duration, _options.MinSceneSeconds, _options.MaxSceneSeconds);

            offsets[i] = cursor;
            durations[i] = duration;
            cursor += duration;
        }

        // One segmentation pass over the whole video keeps cue timing coherent
        // across scene boundaries. The scene duration is passed so a scene with
        // no narration timing (e.g. original clip audio is kept) still gets
        // captions spread across its own length.
        var captionInputs = request.Scenes
            .Select((s, i) => new SceneCaptionInput(s.CaptionText, s.NarrationTiming, offsets[i], durations[i]))
            .ToList();
        var allCues = request.Captions.Enabled
            ? _captionSegmentation.Segment(captionInputs, request.Captions, _captionOptions)
            : Array.Empty<CaptionCue>();

        var scenes = new List<TimelineScene>(request.Scenes.Count);
        for (var i = 0; i < request.Scenes.Count; i++)
        {
            var input = request.Scenes[i];
            var start = offsets[i];
            var end = start + durations[i];

            var motion = input.IsStillImage
                ? MotionCycle[stillIndex++ % MotionCycle.Length]
                : SceneMotion.None;

            var sceneCues = allCues
                .Where(c => c.StartSeconds >= start - 0.001 && c.StartSeconds < end - 0.001)
                .ToList();

            scenes.Add(new TimelineScene(
                input.SceneNumber,
                input.VisualAbsolutePath,
                input.IsStillImage,
                motion,
                start,
                durations[i],
                input.VoiceAbsolutePath,
                i == 0 ? TransitionKind.None : TransitionKind.Fade,
                sceneCues,
                input.AudioSource));
        }

        var total = cursor;
        var narrationPaths = request.Scenes
            .Where(s => !string.IsNullOrWhiteSpace(s.VoiceAbsolutePath))
            .Select(s => s.VoiceAbsolutePath!)
            .ToList();

        var mix = _audioMixing.BuildSpec(new AudioMixRequest(
            request.MusicPath,
            request.Sfx,
            request.AmbiencePath,
            total));

        return new Timeline(
            scenes,
            allCues,
            mix,
            total,
            _options.TargetMinTotalSeconds,
            _options.TargetMaxTotalSeconds);
    }
}
