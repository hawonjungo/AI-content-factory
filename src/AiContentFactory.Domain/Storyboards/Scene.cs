using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.Storyboards;

public class Scene : BaseEntity
{
    public Guid StoryboardId { get; private set; }
    public int SceneNumber { get; private set; }
    public int DurationSeconds { get; private set; }
    public string Narration { get; private set; } = string.Empty;
    public string VisualDescription { get; private set; } = string.Empty;
    public string CameraDirection { get; private set; } = string.Empty;
    public string? VisualStyle { get; private set; }
    public string? GenerationPrompt { get; private set; }
    public string? NegativePrompt { get; private set; }
    public SceneVisualType VisualType { get; private set; }
    public string? Provider { get; private set; }
    public SceneStatus Status { get; private set; } = SceneStatus.Pending;

    private Scene()
    {
        // EF Core
    }

    internal static Scene Create(Guid storyboardId, int sceneNumber, int durationSeconds, string narration, string visualDescription, string cameraDirection, SceneVisualType visualType) => new()
    {
        StoryboardId = storyboardId,
        SceneNumber = sceneNumber,
        DurationSeconds = durationSeconds,
        Narration = narration ?? string.Empty,
        VisualDescription = visualDescription ?? string.Empty,
        CameraDirection = cameraDirection ?? string.Empty,
        VisualType = visualType
    };

    public void UpdateContent(int durationSeconds, string narration, string visualDescription, string cameraDirection, SceneVisualType visualType)
    {
        DurationSeconds = durationSeconds;
        Narration = narration ?? string.Empty;
        VisualDescription = visualDescription ?? string.Empty;
        CameraDirection = cameraDirection ?? string.Empty;
        VisualType = visualType;
        Touch();
    }

    public void SetGenerationPrompt(string prompt, string? negativePrompt, string? visualStyle, string provider)
    {
        GenerationPrompt = prompt;
        NegativePrompt = negativePrompt;
        VisualStyle = visualStyle;
        Provider = provider;
        Status = SceneStatus.PromptReady;
        Touch();
    }

    public void MarkGenerating()
    {
        Status = SceneStatus.Generating;
        Touch();
    }

    public void MarkGenerated()
    {
        Status = SceneStatus.Generated;
        Touch();
    }

    public void MarkFailed()
    {
        Status = SceneStatus.Failed;
        Touch();
    }
}
