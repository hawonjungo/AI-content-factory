namespace AiContentFactory.Domain.Storyboards;

/// <summary>
/// A deterministic camera behaviour for a video scene. The prompt builder turns
/// exactly one of these into a single explicit natural-language camera line -
/// no "push-in or handheld drift" ambiguity. <see cref="Unspecified"/> means the
/// builder picks its house default (a slow push-in).
/// </summary>
public enum CameraMovement
{
    Unspecified = 0,
    Static = 1,
    SlowPushIn = 2,
    SlowPullOut = 3,
    HandheldFollow = 4,
    SideTracking = 5,
    ForwardTracking = 6,
    OverShoulder = 7
}
