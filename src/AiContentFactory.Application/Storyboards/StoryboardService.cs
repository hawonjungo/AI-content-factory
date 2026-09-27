using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Logging;

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

    /// <summary>Approves the scene's current Keyframe (must be Generated first) so it can anchor a video generation. Throws <see cref="DomainException"/> otherwise.</summary>
    Task<StoryboardResponse> ApproveKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);

    /// <summary>Saves a hand-edited motion prompt for animating the scene's approved Keyframe. Blank = "compose one from the scene's action text at generation time".</summary>
    Task<StoryboardResponse> SetMotionPromptAsync(Guid contentProjectId, Guid sceneId, string? motionPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the prompt agent for one scene and stores the result, so the user
    /// can see a suggested prompt in the clip list and then tweak it. Returns
    /// the storyboard with that scene's GenerationPrompt populated.
    /// </summary>
    Task<StoryboardResponse> SuggestScenePromptAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <see cref="SuggestScenePromptAsync"/> once for every scene that
    /// doesn't have a prompt yet - the same "unprompted" condition
    /// <c>FlowGenerationPlanService.IsUnprompted</c> uses (no
    /// <c>GenerationPrompt</c> AND no <c>VisualDescription</c>), so this never
    /// re-bills a scene that already has either. Runs sequentially, one
    /// billable <c>PromptAgent</c> call per unprompted scene; a single scene's
    /// failure is logged and does not abort the batch - already-succeeded
    /// scenes keep their generated prompts. Intended to run inside a
    /// background job (see <c>SuggestAllScenePromptsJob</c>), not a
    /// synchronous HTTP call.
    /// </summary>
    Task<BulkPromptSuggestionResult> SuggestAllScenePromptsAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="SuggestAllScenePromptsAsync(Guid, CancellationToken)"/>,
    /// but with <paramref name="includePrompted"/> true EVERY scene is
    /// re-suggested (one billable call per scene) - so scenes prompted before
    /// framing/on-screen detection existed get them. Hand-written prompts are
    /// overwritten, so the UI must confirm first.
    /// </summary>
    Task<BulkPromptSuggestionResult> SuggestAllScenePromptsAsync(Guid contentProjectId, bool includePrompted, CancellationToken cancellationToken = default);

    /// <summary>Hand-sets the framing for one scene (overrides the prompt agent's choice).</summary>
    Task<StoryboardResponse> SetSceneShotSizeAsync(Guid contentProjectId, Guid sceneId, ShotSize shotSize, CancellationToken cancellationToken = default);

    /// <summary>Hand-sets whether a recurring character is visible in one scene. Null = back to "not decided" (legacy heuristic).</summary>
    Task<StoryboardResponse> SetSceneCharacterOnScreenAsync(Guid contentProjectId, Guid sceneId, bool? characterOnScreen, CancellationToken cancellationToken = default);
}

public class StoryboardService : IStoryboardService
{
    /// <summary>Progress stage key for the bulk "suggest prompts for every unprompted scene" action.</summary>
    public const string PromptsStage = "scene-prompts";

    private readonly IStoryboardRepository _repository;
    private readonly Agents.IPromptAgent _promptAgent;
    private readonly ContentProjects.IContentProjectRepository _projectRepository;
    private readonly Stories.IStoryVisualContextResolver _storyVisualContextResolver;
    private readonly ILogger<StoryboardService> _logger;

    public StoryboardService(
        IStoryboardRepository repository,
        Agents.IPromptAgent promptAgent,
        ContentProjects.IContentProjectRepository projectRepository,
        Stories.IStoryVisualContextResolver storyVisualContextResolver,
        ILogger<StoryboardService> logger)
    {
        _repository = repository;
        _promptAgent = promptAgent;
        _projectRepository = projectRepository;
        _storyVisualContextResolver = storyVisualContextResolver;
        _logger = logger;
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

    public Task<StoryboardResponse> SetSceneShotSizeAsync(Guid contentProjectId, Guid sceneId, ShotSize shotSize, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetShotSize(shotSize), cancellationToken);

    public Task<StoryboardResponse> SetSceneCharacterOnScreenAsync(Guid contentProjectId, Guid sceneId, bool? characterOnScreen, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetCharacterOnScreen(characterOnScreen), cancellationToken);

    public Task<StoryboardResponse> ApproveKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.ApproveKeyframe(), cancellationToken);

    public Task<StoryboardResponse> SetMotionPromptAsync(Guid contentProjectId, Guid sceneId, string? motionPrompt, CancellationToken cancellationToken = default) =>
        MutateSceneAsync(contentProjectId, sceneId, scene => scene.SetMotionPrompt(motionPrompt), cancellationToken);

    public async Task<StoryboardResponse> SuggestScenePromptAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Content project not found.");
        var style = Presets.PresetCatalog.ResolveStyle(project.StylePresetId);
        var storyVisualContext = await _storyVisualContextResolver.ResolveAsync(contentProjectId, cancellationToken);
        var referenceNames = await _storyVisualContextResolver.GetStoryCastAndLocationNamesAsync(contentProjectId, cancellationToken);

        var output = await _promptAgent.GenerateAsync(
            new Agents.PromptAgentInput(
                scene.Narration,
                scene.VisualDescription,
                scene.CameraDirection,
                scene.VisualType,
                style.VisualStyleGuidance,
                style.NegativePrompt,
                storyVisualContext,
                referenceNames,
                PreviousShotOf(storyboard, scene)),
            cancellationToken);

        // The agent now returns structured data: the primary action goes in the
        // generation prompt, the chosen camera/framing are persisted separately.
        // The deterministic VideoPromptBuilder assembles the final prompt at
        // generation / Flow-plan time.
        scene.SetGenerationPrompt(output.Action, output.NegativePrompt, output.VisualStyle, "gemini");
        if (output.Camera != Domain.Storyboards.CameraMovement.Unspecified)
        {
            scene.SetCameraMovement(output.Camera);
        }

        if (output.Shot != Domain.Storyboards.ShotSize.Unspecified)
        {
            scene.SetShotSize(output.Shot);
        }

        // Which recurring characters/locations the SHOT shows replaces the
        // narration-name match from clip planning: a name said in the voiceover
        // is not necessarily on screen, and an on-screen character is often not
        // named. Only when names were offered (Story projects) - otherwise the
        // existing tags are left alone.
        if (output.VisibleReferenceNames is not null)
        {
            scene.SetRelevantReferenceLabels(output.VisibleReferenceNames);
        }

        if (output.CharacterOnScreen is not null)
        {
            scene.SetCharacterOnScreen(output.CharacterOnScreen);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        var fresh = await _repository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after generating the prompt.");
        return StoryboardResponse.FromDomain(fresh);
    }

    public Task<BulkPromptSuggestionResult> SuggestAllScenePromptsAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        SuggestAllScenePromptsAsync(contentProjectId, includePrompted: false, cancellationToken);

    public async Task<BulkPromptSuggestionResult> SuggestAllScenePromptsAsync(Guid contentProjectId, bool includePrompted, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Content project not found.");

        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
        var total = scenes.Count;
        var targetSceneIds = scenes.Where(s => includePrompted || IsSceneUnprompted(s)).Select(s => s.Id).ToList();
        var alreadyPrompted = total - targetSceneIds.Count;

        if (targetSceneIds.Count == 0)
        {
            _logger.LogInformation(
                "Bulk prompt suggestion skipped for ContentProject {ContentProjectId}: all {Total} scenes already have a prompt",
                contentProjectId, total);

            // ReserveJobAsync already marked the project busy before this ran -
            // clear it even on this "nothing to do" path, so the project isn't
            // left stuck busy forever.
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);

            return new BulkPromptSuggestionResult(total, 0, 0, alreadyPrompted, Array.Empty<string>());
        }

        var succeeded = 0;
        var errors = new List<string>();

        try
        {
            project.ReportProgress(PromptsStage, 0, targetSceneIds.Count, $"Đang gợi ý prompt cho {targetSceneIds.Count} cảnh");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            for (var i = 0; i < targetSceneIds.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sceneId = targetSceneIds[i];

                try
                {
                    // Reuse the existing single-scene action as-is - same
                    // PromptAgent call, same persistence, same status update.
                    await SuggestScenePromptAsync(contentProjectId, sceneId, cancellationToken);
                    succeeded++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Treat the AI provider as unreliable: one scene's failure
                    // (e.g. AgentGenerationException) must not lose the
                    // prompts already generated for the other scenes in this
                    // batch, so log and keep going instead of aborting.
                    _logger.LogError(
                        ex,
                        "Bulk prompt suggestion failed for Scene {SceneId} in ContentProject {ContentProjectId} - continuing with remaining scenes",
                        sceneId, contentProjectId);
                    errors.Add($"Scene {sceneId}: {ex.Message}");
                }

                project.ReportProgress(PromptsStage, i + 1, targetSceneIds.Count, $"Đã xong {i + 1}/{targetSceneIds.Count} cảnh");
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Only reached for something unexpected outside the per-scene
            // try/catch above (e.g. a DB failure mid-loop) - re-fetch fresh
            // rather than reuse the possibly-dirty tracked instance, and
            // release the busy lock ReserveJobAsync put in place so the
            // project isn't stuck forever.
            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null)
            {
                freshProject.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }

        var failed = targetSceneIds.Count - succeeded;
        _logger.LogInformation(
            "Bulk prompt suggestion complete for ContentProject {ContentProjectId}: {Succeeded} succeeded, {Failed} failed, {AlreadyPrompted} already prompted (of {Total} scenes)",
            contentProjectId, succeeded, failed, alreadyPrompted, total);

        return new BulkPromptSuggestionResult(total, succeeded, failed, alreadyPrompted, errors);
    }

    /// <summary>
    /// Same "no vetted visual prompt and no written visual description"
    /// condition as <c>FlowGenerationPlanService.IsUnprompted</c> - kept in
    /// sync deliberately so "will the bulk action touch scene X" and "will
    /// the Flow plan leave scene X's prompt blank" always agree.
    /// </summary>
    /// <summary>
    /// The previous scene's framing + camera for the prompt agent's "vary
    /// consecutive shots" rule - null for the first scene or when the previous
    /// scene has not been prompted yet (nothing meaningful to vary from).
    /// </summary>
    private static string? PreviousShotOf(Storyboard storyboard, Scene scene)
    {
        var previous = storyboard.Scenes
            .Where(s => s.SceneNumber < scene.SceneNumber)
            .OrderByDescending(s => s.SceneNumber)
            .FirstOrDefault();

        if (previous is null || string.IsNullOrWhiteSpace(previous.GenerationPrompt))
        {
            return null;
        }

        return $"{previous.ShotSize}, {previous.CameraMovement}";
    }

    private static bool IsSceneUnprompted(Scene scene) =>
        string.IsNullOrWhiteSpace(scene.GenerationPrompt) && string.IsNullOrWhiteSpace(scene.VisualDescription);

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
