namespace AiContentFactory.Domain.Storyboards;

/// <summary>
/// What kind of visual should fill this scene. Deliberately broader than
/// "AI video" so the Asset Selection Agent (Phase 4) can optimize cost by
/// choosing cheaper alternatives for exposition/B-roll scenes.
/// </summary>
public enum SceneVisualType
{
    AiVideo = 0,
    AiImage = 1,
    ExistingFootage = 2,
    MotionGraphic = 3,
    TextAnimation = 4,
    Diagram = 5
}

public enum SceneStatus
{
    Pending = 0,
    PromptReady = 1,
    Generating = 2,
    Generated = 3,
    Approved = 4,
    Failed = 5
}

/// <summary>
/// Lifecycle of a scene's optional Keyframe (the still image a two-stage
/// image-to-video generation animates from - see <see cref="Scene.KeyframeAssetId"/>).
/// Deliberately separate from <see cref="SceneStatus"/>, which continues to
/// track the scene's own final-visual generation (image or video, whichever
/// <see cref="Scene.VisualType"/> is) exactly as before - a scene that never
/// uses the Keyframe workflow simply stays <see cref="None"/> forever and
/// nothing about its existing behaviour changes.
/// </summary>
public enum KeyframeStatus
{
    None = 0,
    Generating = 1,
    Generated = 2,
    Approved = 3,
    Failed = 4
}
