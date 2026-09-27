using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Generation;

public interface IAssetGenerationService
{
    /// <summary>
    /// Generates a Veo clip for every scene in the storyboard that doesn't
    /// already have a ready visual (a scene the user manually uploaded a Flow
    /// clip for, or a rerun after a partial failure, is left untouched), all
    /// using the project's reference images for character/style consistency,
    /// plus a TTS voice-over of each scene's narration. Checks budget before
    /// every Veo call and stops (marking the project Failed) if the monthly
    /// limit is hit partway through - already-generated scenes keep their assets.
    /// </summary>
    Task RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class AssetGenerationService : IAssetGenerationService
{
    public const string ClipsStage = "clips";

    /// <summary>Progress stage key for reference-image generation (now the "Asset Reference" wizard step, not auto).</summary>
    public const string ReferencesStage = "references";

    private readonly IContentProjectRepository _projectRepository;
    private readonly IStoryboardService _storyboardService;
    private readonly IAssetService _assetService;
    private readonly ISceneAssetGenerator _sceneGenerator;
    private readonly ILogger<AssetGenerationService> _logger;

    public AssetGenerationService(
        IContentProjectRepository projectRepository,
        IStoryboardService storyboardService,
        IAssetService assetService,
        ISceneAssetGenerator sceneGenerator,
        ILogger<AssetGenerationService> logger)
    {
        _projectRepository = projectRepository;
        _storyboardService = storyboardService;
        _assetService = assetService;
        _sceneGenerator = sceneGenerator;
        _logger = logger;
    }

    public async Task RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        if (storyboard.Scenes.Count == 0)
        {
            throw new InvalidOperationException("No clips planned yet - set up the clip plan (total duration / clip count) first.");
        }

        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();

        try
        {
            project.TransitionToIfNeeded(ContentProjectStatus.Generating);
            await _projectRepository.SaveChangesAsync(cancellationToken);

            // Reference images are now a dedicated wizard step ("Asset
            // Reference"): the user has already generated/uploaded/approved (or
            // skipped) Character + Environment before this job can be enqueued,
            // and SceneAssetGenerator.BuildContextAsync loads the approved ones.

            project.ReportProgress(ClipsStage, 0, scenes.Count, $"Chuẩn bị dựng {scenes.Count} clip");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            var context = await _sceneGenerator.BuildContextAsync(project, cancellationToken);
            var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

            for (var i = 0; i < scenes.Count; i++)
            {
                var scene = scenes[i];

                // A scene that already has a ready visual (e.g. a Flow clip the user
                // uploaded by hand), or one the user flagged "already has a video",
                // must not be silently overwritten and re-billed - only scenes still
                // missing a visual go through Veo/the image provider. Voice-over is
                // still generated below either way.
                if (scene.SkipGeneration || HasReadyVisual(assets, scene.Id))
                {
                    _logger.LogInformation("Scene {SceneId} already has a video / is flagged skip-generation - skipping regeneration", scene.Id);
                }
                else
                {
                    await _sceneGenerator.GenerateClipAsync(context, scene, refreshPrompt: false, cancellationToken);
                }

                // Same idempotency for narration: a scene whose voice-over already
                // succeeded must not be redone on retry - otherwise a retry after a
                // mid-run failure (e.g. a Gemini quota error) re-fails immediately on
                // the first already-completed scene instead of ever reaching the one
                // that actually still needs work.
                //
                // Only "generate new voice" pre-generates TTS for every scene
                // here. "Smart" defers it to render time and only for clips that
                // actually lack original audio; "mute" never uses TTS.
                if (project.AudioMode == AudioMode.Generated && !HasReadyAsset(assets, scene.Id, AssetType.Voice))
                {
                    await _sceneGenerator.GenerateVoiceAsync(context, scene, cancellationToken);
                }

                // Reported after each clip rather than at the end: Veo runs
                // sequentially at 1-3 minutes a clip, so this is the only
                // signal the user has that anything is happening.
                project.ReportProgress(ClipsStage, i + 1, scenes.Count, $"Đã xong clip {i + 1}/{scenes.Count}");
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            project.TransitionToIfNeeded(ContentProjectStatus.Editing);
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Asset generation complete for ContentProject {ContentProjectId}", contentProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Asset generation failed for ContentProject {ContentProjectId}", contentProjectId);

            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null)
            {
                freshProject.TransitionToIfNeeded(ContentProjectStatus.Failed);
                freshProject.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    private static bool HasReadyVisual(IReadOnlyList<AssetResponse> assets, Guid sceneId) =>
        assets.Any(a => a.SceneId == sceneId
            && a.Status == nameof(AssetStatus.Ready)
            && (a.Type == nameof(AssetType.Video) || a.Type == nameof(AssetType.Image)));

    private static bool HasReadyAsset(IReadOnlyList<AssetResponse> assets, Guid sceneId, AssetType type) =>
        assets.Any(a => a.SceneId == sceneId && a.Status == nameof(AssetStatus.Ready) && a.Type == type.ToString());
}
