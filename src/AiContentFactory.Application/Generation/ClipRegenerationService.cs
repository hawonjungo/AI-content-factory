using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Generation;

/// <param name="NarrationOverride">
/// New narration for this clip. Changing it forces the voice-over to be
/// regenerated too - a clip whose audio says something different from its
/// caption is worse than the clip the user was trying to fix.
/// </param>
/// <param name="RegenerateVoice">
/// Redo the voice-over even when the narration is unchanged. Off by default:
/// the usual reason to rerun a clip is that the visuals came out wrong, and
/// the existing audio is fine.
/// </param>
public record RegenerateClipRequest(string? NarrationOverride = null, bool RegenerateVoice = false);

public interface IClipRegenerationService
{
    Task RunAsync(Guid contentProjectId, Guid sceneId, RegenerateClipRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reruns exactly one scene. Without this, a single bad clip in a five-clip
/// project means rerunning all five - 10+ minutes and five clips' worth of Veo
/// credits to fix one.
///
/// Shares SceneAssetGenerator with the full run, so a clip regenerated on its
/// own is produced by the same code, presets, and cost tracking as one
/// produced by the batch.
/// </summary>
public class ClipRegenerationService : IClipRegenerationService
{
    public const string ClipStage = "clip";

    private readonly IContentProjectRepository _projectRepository;
    private readonly IStoryboardService _storyboardService;
    private readonly ISceneAssetGenerator _sceneGenerator;
    private readonly ILogger<ClipRegenerationService> _logger;

    public ClipRegenerationService(
        IContentProjectRepository projectRepository,
        IStoryboardService storyboardService,
        ISceneAssetGenerator sceneGenerator,
        ILogger<ClipRegenerationService> logger)
    {
        _projectRepository = projectRepository;
        _storyboardService = storyboardService;
        _sceneGenerator = sceneGenerator;
        _logger = logger;
    }

    public async Task RunAsync(Guid contentProjectId, Guid sceneId, RegenerateClipRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException($"Clip '{sceneId}' was not found on this project.");

        // A clip the user already has a video for is locked: no rerun, no job,
        // no provider call. They remove the imported clip (which clears the flag)
        // to build it with Veo instead.
        if (scene.SkipGeneration)
        {
            throw new InvalidOperationException(
                "Clip này đã được đánh dấu \"đã có video\" - bỏ đánh dấu hoặc xoá video đã tải lên trước khi tạo lại.");
        }

        // Status is captured before we move to Generating so a failure can put
        // the project back where it was instead of parking it in Failed - the
        // other clips and any existing render are still perfectly good.
        var previousStatus = project.Status;
        var narrationChanged = !string.IsNullOrWhiteSpace(request.NarrationOverride)
            && !string.Equals(request.NarrationOverride!.Trim(), scene.Narration, StringComparison.Ordinal);

        try
        {
            if (narrationChanged)
            {
                var updated = await _storyboardService.UpdateSceneAsync(
                    contentProjectId,
                    sceneId,
                    new UpdateSceneRequest(
                        scene.DurationSeconds,
                        request.NarrationOverride!.Trim(),
                        scene.VisualDescription,
                        scene.CameraDirection,
                        SceneVisualType.AiVideo),
                    cancellationToken);

                scene = updated.Scenes.First(s => s.Id == sceneId);
            }

            project.TransitionToIfNeeded(ContentProjectStatus.Generating);
            project.ReportProgress(ClipStage, 0, 1, $"Đang tạo lại clip {scene.SceneNumber}");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            var context = await _sceneGenerator.BuildContextAsync(project, cancellationToken);

            // Always a fresh prompt: reusing the stored one would largely
            // reproduce the clip the user just rejected.
            await _sceneGenerator.GenerateClipAsync(context, scene, refreshPrompt: true, cancellationToken);

            // "Mute" has no voice track. "Generate new voice" and "Smart" both
            // can - Smart uses it only if this clip lacks original audio, which
            // the renderer decides, so regenerating it here is safe either way.
            if (project.AudioMode != AudioMode.Muted && (request.RegenerateVoice || narrationChanged))
            {
                await _sceneGenerator.GenerateVoiceAsync(context, scene, cancellationToken);
            }

            project.TransitionToIfNeeded(ContentProjectStatus.Editing);
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Regenerated clip {SceneNumber} ({SceneId}) for ContentProject {ContentProjectId}",
                scene.SceneNumber, sceneId, contentProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clip regeneration failed for scene {SceneId} on ContentProject {ContentProjectId}", sceneId, contentProjectId);

            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null)
            {
                RestoreStatus(freshProject, previousStatus);
                freshProject.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    /// <summary>
    /// Generating can only go to Editing or Failed, so an AwaitingApproval
    /// project has to be walked back through Editing. If even that is
    /// rejected, fall back to Failed rather than leaving the project stuck
    /// mid-generation forever.
    /// </summary>
    private static void RestoreStatus(ContentProject project, ContentProjectStatus previousStatus)
    {
        try
        {
            project.TransitionToIfNeeded(ContentProjectStatus.Editing);

            if (previousStatus == ContentProjectStatus.AwaitingApproval)
            {
                project.TransitionToIfNeeded(ContentProjectStatus.AwaitingApproval);
            }
        }
        catch (Domain.Exceptions.DomainException)
        {
            project.TransitionToIfNeeded(ContentProjectStatus.Failed);
        }
    }
}
