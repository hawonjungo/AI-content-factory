using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Audio;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Rendering;

/// <summary>
/// BurnCaptions is gone: whether captions appear is now
/// CaptionSettings.Enabled on the project, set by picking the "Không phụ đề"
/// preset or toggling it in the caption editor.
/// </summary>
public record RenderProjectRequest();

public interface IRenderService
{
    Task RunAsync(Guid contentProjectId, RenderProjectRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a single still showing the project's current caption styling
    /// over its first clip, and registers it as the project's caption-preview
    /// asset. Returns that asset, or null when there is nothing to draw over yet.
    /// </summary>
    Task<AssetResponse?> RenderCaptionPreviewAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Audio-first composition entry point. The order is fixed:
/// narration audio -> validate -> actual timing -> caption segmentation ->
/// timeline (narration is the duration authority) -> video composition ->
/// final quality validation. The project only reaches AwaitingApproval if the
/// finished MP4 passes <see cref="IVideoQualityValidator"/>, so a video with
/// captions but no narrator audio can never be marked ready.
///
/// Re-runnable independently of asset generation - re-rendering after changing
/// the caption preset or the music reuses the same clips and costs nothing.
/// </summary>
public class RenderService : IRenderService
{
    public const string RenderStage = "render";

    private readonly IContentProjectRepository _projectRepository;
    private readonly IStoryboardService _storyboardService;
    private readonly IAssetService _assetService;
    private readonly IAssetReferenceRepository _assetReferenceRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IVideoRenderer _renderer;
    private readonly IAudioValidator _audioValidator;
    private readonly IAudioTimingService _audioTiming;
    private readonly ITimelineService _timelineService;
    private readonly IVideoCompositionService _composition;
    private readonly ILogger<RenderService> _logger;

    public RenderService(
        IContentProjectRepository projectRepository,
        IStoryboardService storyboardService,
        IAssetService assetService,
        IAssetReferenceRepository assetReferenceRepository,
        IFileStorage fileStorage,
        IVideoRenderer renderer,
        IAudioValidator audioValidator,
        IAudioTimingService audioTiming,
        ITimelineService timelineService,
        IVideoCompositionService composition,
        ILogger<RenderService> logger)
    {
        _projectRepository = projectRepository;
        _storyboardService = storyboardService;
        _assetService = assetService;
        _assetReferenceRepository = assetReferenceRepository;
        _fileStorage = fileStorage;
        _renderer = renderer;
        _audioValidator = audioValidator;
        _audioTiming = audioTiming;
        _timelineService = timelineService;
        _composition = composition;
        _logger = logger;
    }

    public async Task RunAsync(Guid contentProjectId, RenderProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        try
        {
            project.TransitionToIfNeeded(ContentProjectStatus.Editing);
            project.ReportProgress(RenderStage, 0, 1, "Đang ghép video");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            var orderedScenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
            if (orderedScenes.Count == 0)
            {
                throw new InvalidOperationException("No scenes to render - generate the clip plan and clips first.");
            }

            var timelineInputs = new List<TimelineSceneInput>();
            var audioProblems = new List<string>();

            foreach (var scene in orderedScenes)
            {
                var video = CurrentAsset(assets, scene.Id, AssetType.Video);
                var still = CurrentSceneStill(assets, scene.Id);
                var visual = Newer(video, still);
                var isStill = visual is not null && ReferenceEquals(visual, still);

                if (visual is null || string.IsNullOrWhiteSpace(visual.FilePath))
                {
                    throw new InvalidOperationException($"Scene {scene.SceneNumber} has no ready clip - generate the clips first.");
                }

                var voice = CurrentAsset(assets, scene.Id, AssetType.Voice);
                var timing = await ResolveNarrationTimingAsync(scene, voice, audioProblems, cancellationToken);

                timelineInputs.Add(new TimelineSceneInput(
                    SceneNumber: scene.SceneNumber,
                    VisualAbsolutePath: _fileStorage.GetAbsolutePath(visual.FilePath),
                    IsStillImage: isStill,
                    VoiceAbsolutePath: voice?.FilePath is null ? null : _fileStorage.GetAbsolutePath(voice.FilePath),
                    NarrationTiming: timing,
                    CaptionText: string.IsNullOrWhiteSpace(scene.CaptionText) ? scene.Narration : scene.CaptionText!));
            }

            // Audio-first gate: a scene that is supposed to be narrated but has
            // no usable voice track fails the whole render - we never ship a
            // captions-only video.
            if (audioProblems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Narration audio is missing or invalid, cannot compose the video: " + string.Join("; ", audioProblems));
            }

            var music = assets
                .Where(a => a.SceneId is null && a.Status == nameof(AssetStatus.Ready) && a.Type == nameof(AssetType.Music))
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            var timeline = _timelineService.Build(new TimelineBuildRequest(
                timelineInputs,
                project.Captions,
                MusicPath: music?.FilePath is null ? null : _fileStorage.GetAbsolutePath(music.FilePath)));

            var outputKey = $"content-projects/{contentProjectId}/final.mp4";
            var outputAbsolutePath = _fileStorage.GetAbsolutePath(outputKey);

            var result = await _composition.ComposeAsync(new CompositionRequest(
                ContentProjectId: contentProjectId,
                Timeline: timeline,
                Captions: project.Captions,
                OutputAbsolutePath: outputAbsolutePath,
                BackgroundMusicAbsolutePath: music?.FilePath is null ? null : _fileStorage.GetAbsolutePath(music.FilePath),
                StoryboardSceneCount: orderedScenes.Count,
                ScenesWithVisual: timelineInputs.Count),
                cancellationToken);

            // Only one final video is current at a time.
            await _assetService.SupersedeSceneAssetsAsync(contentProjectId, null, AssetType.Video, cancellationToken);

            await _assetService.CreateAsync(
                contentProjectId,
                new CreateAssetRequest(SceneId: null, Type: AssetType.Video, Provider: "ffmpeg", Prompt: null, FilePath: outputKey, DurationSeconds: result.DurationSeconds, Width: 1080, Height: 1920),
                cancellationToken);

            // Persist the validation outcome so the wizard's Preview/Export
            // steps can show exactly what passed.
            project.RecordRenderValidation(RenderValidationSummary.Create(
                ok: true,
                summary: result.Validation.Summary,
                errors: result.Validation.Errors,
                warnings: result.Validation.Warnings,
                durationSeconds: result.DurationSeconds));

            project.TransitionToIfNeeded(ContentProjectStatus.AwaitingApproval);
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Render complete for ContentProject {ContentProjectId}: {Duration:0.0}s, {Cues} caption cues, {Scenes} scenes -> {Output}",
                contentProjectId, result.DurationSeconds, timeline.AllCues.Count, timeline.SceneCount, outputKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Render failed for ContentProject {ContentProjectId}", contentProjectId);

            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null)
            {
                // Record why it failed so the wizard shows actionable errors, not a bare badge.
                var (summary, errors, warnings) = ex is VideoCompositionException vce
                    ? (vce.Report.Summary, vce.Report.Errors, vce.Report.Warnings)
                    : ("render failed: " + ex.Message, (IReadOnlyList<string>)new[] { ex.Message }, Array.Empty<string>());

                freshProject.RecordRenderValidation(RenderValidationSummary.Create(false, summary, errors, warnings, 0));
                freshProject.TransitionToIfNeeded(ContentProjectStatus.Failed);
                freshProject.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }

    /// <summary>
    /// Real narration timing for a scene: validates the generated voice track
    /// (readable, non-zero, not silent) and computes word/sentence timestamps
    /// from its actual measured duration. A narrated scene with a broken voice
    /// track is added to <paramref name="problems"/> so the render fails.
    /// </summary>
    private async Task<Audio.AudioTiming> ResolveNarrationTimingAsync(
        SceneResponse scene,
        AssetResponse? voice,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var expectsNarration = !string.IsNullOrWhiteSpace(scene.Narration);

        if (voice?.FilePath is null)
        {
            if (expectsNarration)
            {
                problems.Add($"scene {scene.SceneNumber} has narration text but no voice asset");
            }

            return Audio.AudioTiming.Empty;
        }

        AudioValidationResult validation;
        try
        {
            await using var stream = await _fileStorage.GetAsync(voice.FilePath, cancellationToken);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            validation = _audioValidator.Validate(memory.ToArray(), "audio/wav");
        }
        catch (Exception ex)
        {
            problems.Add($"scene {scene.SceneNumber} voice track could not be read ({ex.Message})");
            return Audio.AudioTiming.Empty;
        }

        if (!validation.IsValid)
        {
            problems.Add($"scene {scene.SceneNumber} voice track is invalid ({validation.Error})");
            return Audio.AudioTiming.Empty;
        }

        var measured = validation.DurationSeconds > 0
            ? validation.DurationSeconds
            : voice.DurationSeconds ?? 0;

        if (measured <= 0)
        {
            problems.Add($"scene {scene.SceneNumber} voice track has zero duration");
            return Audio.AudioTiming.Empty;
        }

        // Reuse the timing captured at TTS time when it still matches the audio
        // on disk (i.e. the voice wasn't regenerated since); otherwise recompute.
        var persisted = _audioTiming.Deserialize(scene.AudioTimingJson);
        if (persisted.HasTiming && Math.Abs(persisted.TotalSeconds - measured) <= 0.5)
        {
            return persisted;
        }

        return _audioTiming.Compute(scene.Narration, measured);
    }

    public async Task<AssetResponse?> RenderCaptionPreviewAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        var firstScene = storyboard.Scenes.OrderBy(s => s.SceneNumber).FirstOrDefault();
        if (firstScene is null)
        {
            return null;
        }

        var clipVisual = CurrentAsset(assets, firstScene.Id, AssetType.Video);
        string? backgroundAbsolutePath = clipVisual?.FilePath is not null
            ? _fileStorage.GetAbsolutePath(clipVisual.FilePath)
            : null;

        if (backgroundAbsolutePath is null)
        {
            var refs = await _assetReferenceRepository.GetByProjectAsync(contentProjectId, cancellationToken);
            var approved = refs
                .Where(r => r.Status == AssetReferenceStatus.Approved && !string.IsNullOrWhiteSpace(r.ImagePath))
                .OrderBy(r => r.Type)
                .FirstOrDefault();
            if (approved?.ImagePath is not null)
            {
                backgroundAbsolutePath = _fileStorage.GetAbsolutePath(approved.ImagePath);
            }
        }

        if (backgroundAbsolutePath is null)
        {
            return null;
        }

        var sampleText = string.IsNullOrWhiteSpace(firstScene.CaptionText)
            ? (string.IsNullOrWhiteSpace(firstScene.Narration) ? project.Title : firstScene.Narration)
            : firstScene.CaptionText!;

        var outputKey = $"content-projects/{contentProjectId}/caption-preview-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.png";

        await _renderer.RenderCaptionPreviewAsync(
            new CaptionPreviewRequest(
                backgroundAbsolutePath,
                sampleText,
                project.Captions,
                _fileStorage.GetAbsolutePath(outputKey)),
            cancellationToken);

        foreach (var stale in assets.Where(a => a.Provider == AssetProviders.CaptionPreview))
        {
            await _assetService.DeleteAsync(contentProjectId, stale.Id, cancellationToken);
        }

        return await _assetService.CreateAsync(
            contentProjectId,
            new CreateAssetRequest(null, AssetType.Image, AssetProviders.CaptionPreview, sampleText, outputKey, null, null, null),
            cancellationToken);
    }

    private static AssetResponse? CurrentAsset(IReadOnlyList<AssetResponse> assets, Guid sceneId, AssetType type) =>
        assets
            .Where(a => a.SceneId == sceneId && a.Status == nameof(AssetStatus.Ready) && a.Type == type.ToString())
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

    private static AssetResponse? CurrentSceneStill(IReadOnlyList<AssetResponse> assets, Guid sceneId) =>
        CurrentAsset(assets, sceneId, AssetType.Image);

    private static AssetResponse? Newer(AssetResponse? a, AssetResponse? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.CreatedAt >= b.CreatedAt ? a : b;
    }
}
