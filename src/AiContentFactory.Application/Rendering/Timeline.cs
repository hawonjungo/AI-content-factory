namespace AiContentFactory.Application.Rendering;

/// <summary>Camera move applied to an image-based scene so it never reads as a static slide.</summary>
public enum SceneMotion
{
    None = 0,
    KenBurnsIn = 1,
    KenBurnsOut = 2,
    PanLeft = 3,
    PanRight = 4,
    Parallax = 5
}

public enum TransitionKind
{
    None = 0,
    Fade = 1
}

/// <summary>Where a scene segment's audio comes from - the Step 6 Voice option, resolved per scene.</summary>
public enum SceneAudioSource
{
    /// <summary>The generated TTS voice track (<see cref="TimelineScene.VoiceAbsolutePath"/>), or silence if absent. Original behaviour.</summary>
    Voice = 0,

    /// <summary>The clip's own embedded audio.</summary>
    Clip = 1,

    /// <summary>Forced silence - the clip audio is dropped and no voice is used.</summary>
    Silent = 2
}

/// <param name="StartSeconds">Where the scene begins on the finished timeline.</param>
/// <param name="DurationSeconds">Driven by the scene's narration length (the timeline authority), not the storyboard estimate.</param>
public record TimelineScene(
    int SceneNumber,
    string VisualAbsolutePath,
    bool IsStillImage,
    SceneMotion Motion,
    double StartSeconds,
    double DurationSeconds,
    string? VoiceAbsolutePath,
    TransitionKind TransitionIn,
    IReadOnlyList<CaptionCue> Cues,
    SceneAudioSource AudioSource = SceneAudioSource.Voice)
{
    public double EndSeconds => StartSeconds + DurationSeconds;

    /// <summary>True only when a TTS voice track is the audio bed for this scene.</summary>
    public bool HasNarration => AudioSource == SceneAudioSource.Voice && !string.IsNullOrWhiteSpace(VoiceAbsolutePath);
}

public record Timeline(
    IReadOnlyList<TimelineScene> Scenes,
    IReadOnlyList<CaptionCue> AllCues,
    AudioMixSpec Audio,
    double TotalSeconds,
    double TargetMinSeconds,
    double TargetMaxSeconds)
{
    public bool WithinTargetRange => TotalSeconds >= TargetMinSeconds && TotalSeconds <= TargetMaxSeconds;
    public int SceneCount => Scenes.Count;
}
