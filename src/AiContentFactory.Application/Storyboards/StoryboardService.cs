using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Storyboards;

public interface IStoryboardService
{
    Task<StoryboardResponse> GetOrCreateAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<StoryboardResponse> AddSceneAsync(Guid contentProjectId, CreateSceneRequest request, CancellationToken cancellationToken = default);
    Task<StoryboardResponse> UpdateSceneAsync(Guid contentProjectId, Guid sceneId, UpdateSceneRequest request, CancellationToken cancellationToken = default);
    Task<StoryboardResponse> RemoveSceneAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task SetScenePromptAsync(Guid contentProjectId, Guid sceneId, string prompt, string? negativePrompt, string? visualStyle, string provider, CancellationToken cancellationToken = default);

    /// <summary>Persists the serialized narration timing captured when a scene's TTS ran.</summary>
    Task SetSceneAudioTimingAsync(Guid contentProjectId, Guid sceneId, string? audioTimingJson, CancellationToken cancellationToken = default);

    /// <summary>Switches a single scene between AI video and AI still image (clears its stored prompt).</summary>
    Task<StoryboardResponse> SetSceneVisualTypeAsync(Guid contentProjectId, Guid sceneId, SceneVisualType visualType, CancellationToken cancellationToken = default);

    /// <summary>Sets the Veo model tier ("Fast"/"Lite") for one video scene; blank restores the allocator's pick.</summary>
    Task<StoryboardResponse> SetSceneModelTierAsync(Guid contentProjectId, Guid sceneId, string? modelTier, CancellationToken cancellationToken = default);

    /// <summary>Sets the deterministic camera movement for one scene (drives the "Camera:" line of the video prompt).</summary>
    Task<StoryboardResponse> SetSceneCameraMovementAsync(Guid contentProjectId, Guid sceneId, CameraMovement cameraMovement, CancellationToken cancellationToken = default);

    /// <summary>Saves a hand-edited generation prompt for one scene. Blank prompt = "let the agent write it".</summary>
    Task<StoryboardResponse> SetScenePromptTextAsync(Guid contentProjectId, Guid sceneId, string? prompt, string? negativePrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the prompt agent for one scene and stores the result, so the user
    /// can see a suggested prompt in the clip list and then tweak it. Returns
    /// the storyboard with that scene's GenerationPrompt populated.
    /// </summary>
    Task<StoryboardResponse> SuggestScenePromptAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);
}

public class StoryboardService : IStoryboardService
{
    private readonly IStoryboardRepository _repository;
    private readonly Agents.IPromptAgent _promptAgent;
    private readonly ContentProjects.IContentProjectRepository _projectRepository;

    public StoryboardService(
        IStoryboardRepository repository,
        Agents.IPromptAgent promptAgent,
        ContentProjects.IContentProjectRepository projectRepository)
    {
        _repository = repository;
        _promptAgent = promptAgent;
        _projectRepository = projectRepository;
    }

    public async Task<StoryboardResponse> GetOrCreateAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        if (storyboard is null)
        {
            storyboard = Storyboard.Create(contentProjectId);
            await _repository.AddAsync(storyboard, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return StoryboardResponse.FromDomain(storyboard);
    }

    public async Task<StoryboardResponse> AddSceneAsync(Guid contentProjectId, CreateSceneRequest request, CancellationToken cancellationToken = default)
    {
        var storyboard = await GetOrCreateStoryboardEntityAsync(contentProjectId, cancellationToken);

        storyboard.AddScene(request.DurationSeconds, request.Narration, request.VisualDescription, request.CameraDirection, request.VisualType);
        await _repository.SaveChangesAsync(cancellationToken);

        // Return fresh untracked data to avoid stale state
        var responseStoryboard = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after scene addition.");
        return StoryboardResponse.FromDomain(responseStoryboard);
    }

    public async Task<StoryboardResponse> UpdateSceneAsync(Guid contentProjectId, Guid sceneId, UpdateSceneRequest request, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        scene.UpdateContent(request.DurationSeconds, request.Narration, request.VisualDescription, request.CameraDirection, request.VisualType);
        await _repository.SaveChangesAsync(cancellationToken);

        // Return fresh untracked data to avoid stale state
        var responseStoryboard = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after scene update.");
        return StoryboardResponse.FromDomain(responseStoryboard);
    }

    public async Task<StoryboardResponse> RemoveSceneAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        storyboard.RemoveScene(sceneId);
        await _repository.SaveChangesAsync(cancellationToken);

        // Return fresh untracked data to avoid stale state
        var responseStoryboard = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after scene removal.");
        return StoryboardResponse.FromDomain(responseStoryboard);
    }

    public async Task SetScenePromptAsync(Guid contentProjectId, Guid sceneId, string prompt, string? negativePrompt, string? visualStyle, string provider, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        scene.SetGenerationPrompt(prompt, negativePrompt, visualStyle, provider);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task SetSceneAudioTimingAsync(Guid contentProjectId, Guid sceneId, string? audioTimingJson, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        scene.SetAudioTiming(audioTimingJson);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public Task<StoryboardResponse> SetSceneVisualTypeAsync(Guid contentProjectId, Guid sceneId, SceneVisualType visualType, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetVisualType(visualType), cancellationToken);

    public Task<StoryboardResponse> SetScenePromptTextAsync(Guid contentProjectId, Guid sceneId, string? prompt, string? negativePrompt, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetGenerationPromptText(prompt, negativePrompt), cancellationToken);

    public Task<StoryboardResponse> SetSceneModelTierAsync(Guid contentProjectId, Guid sceneId, string? modelTier, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetModelTier(modelTier), cancellationToken);

    public Task<StoryboardResponse> SetSceneCameraMovementAsync(Guid contentProjectId, Guid sceneId, CameraMovement cameraMovement, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetCameraMovement(cameraMovement), cancellationToken);

    public async Task<StoryboardResponse> SuggestScenePromptAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Content project not found.");
        var style = Presets.PresetCatalog.ResolveStyle(project.StylePresetId);

        var output = await _promptAgent.GenerateAsync(
            new Agents.PromptAgentInput(
                scene.Narration,
                scene.VisualDescription,
                scene.CameraDirection,
                scene.VisualType,
                style.VisualStyleGuidance,
                style.NegativePrompt),
            cancellationToken);

        // The agent now returns structured data: the primary action goes in the
        // generation prompt, the chosen camera is persisted separately. The
        // deterministic VideoPromptBuilder assembles the final prompt at
        // generation / Flow-plan time.
        scene.SetGenerationPrompt(output.Action, output.NegativePrompt, output.VisualStyle, "gemini");
        if (output.Camera != Domain.Storyboards.CameraMovement.Unspecified)
        {
            scene.SetCameraMovement(output.Camera);
        }
        await _repository.SaveChangesAsync(cancellationToken);

        var fresh = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after generating the prompt.");
        return StoryboardResponse.FromDomain(fresh);
    }

    private async Task<StoryboardResponse> MutateSceneAsync(Guid contentProjectId, Guid sceneId, Action<Scene> mutate, CancellationToken cancellationToken)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        mutate(scene);
        await _repository.SaveChangesAsync(cancellationToken);

        var fresh = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after the update.");
        return StoryboardResponse.FromDomain(fresh);
    }

    private async Task<Storyboard> GetOrCreateStoryboardEntityAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        if (storyboard is not null)
        {
            return storyboard;
        }

        storyboard = Storyboard.Create(contentProjectId);
        await _repository.AddAsync(storyboard, cancellationToken);
        return storyboard;
    }
}
