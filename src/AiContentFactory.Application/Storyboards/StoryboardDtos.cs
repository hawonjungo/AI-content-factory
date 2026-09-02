using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Storyboards;

public record CreateSceneRequest(
    int DurationSeconds,
    string Narration,
    string VisualDescription,
    string CameraDirection,
    SceneVisualType VisualType);

public record UpdateSceneRequest(
    int DurationSeconds,
    string Narration,
    string VisualDescription,
    string CameraDirection,
    SceneVisualType VisualType);

public record SceneResponse(
    Guid Id,
    int SceneNumber,
    int DurationSeconds,
    string Narration,
    string? CaptionText,
    string VisualDescription,
    string CameraDirection,
    string CameraMovement,
    string? VisualStyle,
    string? GenerationPrompt,
    string? NegativePrompt,
    string VisualType,
    string? Provider,
    string Status,
    int AiVideoPriority,
    string? ModelTier,
    string? AllocationRationale,
    string GenerationType,
    bool CharacterRequired,
    string? AudioTimingJson,
    bool SkipGeneration)
{
    /// <summary>On-screen caption text: the explicit override if set, otherwise the narration.</summary>
    public string EffectiveCaptionText => string.IsNullOrWhiteSpace(CaptionText) ? Narration : CaptionText;

    /// <summary>AI_VIDEO / AI_IMAGE / STATIC - how this scene's visual is produced.</summary>
    public static string GenerationTypeOf(SceneVisualType visualType) => visualType switch
    {
        SceneVisualType.AiVideo => "AI_VIDEO",
        SceneVisualType.AiImage => "AI_IMAGE",
        _ => "STATIC"
    };

    public static SceneResponse FromDomain(Scene scene) => new(
        scene.Id,
        scene.SceneNumber,
        scene.DurationSeconds,
        scene.Narration,
        scene.CaptionText,
        scene.VisualDescription,
        scene.CameraDirection,
        scene.CameraMovement.ToString(),
        scene.VisualStyle,
        scene.GenerationPrompt,
        scene.NegativePrompt,
        scene.VisualType.ToString(),
        scene.Provider,
        scene.Status.ToString(),
        scene.AiVideoPriority,
        scene.ModelTier,
        scene.AllocationRationale,
        GenerationTypeOf(scene.VisualType),
        // A scene features the protagonist when it is a real motion beat: any
        // AI-video scene, or a still the allocator ranked at/above the Lite bar.
        scene.VisualType == SceneVisualType.AiVideo || scene.AiVideoPriority >= 40,
        scene.AudioTimingJson,
        scene.SkipGeneration);
}

public record StoryboardResponse(
    Guid Id,
    Guid ContentProjectId,
    IReadOnlyList<SceneResponse> Scenes)
{
    public static StoryboardResponse FromDomain(Storyboard storyboard) => new(
        storyboard.Id,
        storyboard.ContentProjectId,
        storyboard.Scenes.OrderBy(s => s.SceneNumber).Select(SceneResponse.FromDomain).ToList());
}
