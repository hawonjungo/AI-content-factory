using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Storyboards;

public class Storyboard : BaseEntity
{
    public Guid ContentProjectId { get; private set; }

    private readonly List<Scene> _scenes = new();
    public IReadOnlyCollection<Scene> Scenes => _scenes.AsReadOnly();

    private Storyboard()
    {
        // EF Core
    }

    public static Storyboard Create(Guid contentProjectId) => new()
    {
        ContentProjectId = contentProjectId
    };

    public Scene AddScene(int durationSeconds, string narration, string visualDescription, string cameraDirection, SceneVisualType visualType)
    {
        var nextSceneNumber = _scenes.Count == 0 ? 1 : _scenes.Max(s => s.SceneNumber) + 1;
        var scene = Scene.Create(Id, nextSceneNumber, durationSeconds, narration, visualDescription, cameraDirection, visualType);
        _scenes.Add(scene);
        Touch();
        return scene;
    }

    public void RemoveScene(Guid sceneId)
    {
        var scene = _scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene is null)
        {
            throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");
        }

        _scenes.Remove(scene);
        Touch();
    }
}
