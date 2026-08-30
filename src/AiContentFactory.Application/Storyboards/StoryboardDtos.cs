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
    string VisualDescription,
    string CameraDirection,
    string? VisualStyle,
    string? GenerationPrompt,
    string? NegativePrompt,
    string VisualType,
    string? Provider,
    string Status)
{
    public static SceneResponse FromDomain(Scene scene) => new(
        scene.Id,
        scene.SceneNumber,
        scene.DurationSeconds,
        scene.Narration,
        scene.VisualDescription,
        scene.CameraDirection,
        scene.VisualStyle,
        scene.GenerationPrompt,
        scene.NegativePrompt,
        scene.VisualType.ToString(),
        scene.Provider,
        scene.Status.ToString());
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
