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
