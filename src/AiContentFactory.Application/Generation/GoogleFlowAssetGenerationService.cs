using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <summary>Keyed-service keys so the Google Flow path can resolve Nano Banana + Veo image-to-video instead of the standard providers.</summary>
public static class GoogleFlowKeys
{
    public const string Provider = "googleflow";
}

/// <summary>
/// Optimized asset generation using Google Flow strategy:
/// 1. Generate Hook-Heavy 20s script (3 scenes: 5s hook + 10s climax + 5s CTA)
/// 2. Generate FREE images via Nano Banana for each scene
/// 3. Convert images to video via Veo 3.1 Image-to-Video (10 credits/video max 50/day)
/// 4. Generate TTS voice-over for each scene
/// 5. Render final video via FFmpeg
///
/// Cost: ~$0.02/video (vs $1.73 traditional) - 98.8% savings!
/// Daily capacity: 5 videos × 20s = 100s of final video
/// </summary>
public interface IGoogleFlowAssetGenerationService
{
    /// <param name="autoHook">Ignore any existing clip plan and let the agent write its own fixed 3-scene hook script.</param>
    Task RunAsync(Guid contentProjectId, string topic, string audience, bool autoHook = false, CancellationToken cancellationToken = default);
}

public class GoogleFlowAssetGenerationService : IGoogleFlowAssetGenerationService
{
    public const string Stage = "googleflow";

    private readonly PricingOptions _pricing;
    private readonly IContentProjectRepository _projectRepository;
    private readonly IStoryboardService _storyboardService;
    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IAssetService _assetService;
    private readonly IHookScriptAgent _hookScriptAgent;
    private readonly ISceneAssetGenerator _sceneGenerator;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly IVideoGenerationProvider _videoProvider;
    private readonly ITtsProvider _ttsProvider;
    private readonly IGoogleFlowQuotaManager _quotaManager;
    private readonly IFileStorage _fileStorage;
    private readonly IAiUsageTracker _usageTracker;
    private readonly ILogger<GoogleFlowAssetGenerationService> _logger;

    public GoogleFlowAssetGenerationService(
        IContentProjectRepository projectRepository,
        IStoryboardService storyboardService,
        IStoryboardRepository storyboardRepository,
        IAssetService assetService,
        IHookScriptAgent hookScriptAgent,
        // Shared with the standard pipeline so both paths load the same
        // approved Character/Environment anchors + style preset once per run
        // instead of Flow scenes generating each image with no anchor at all.
        ISceneAssetGenerator sceneGenerator,
        // The standard image provider is already Nano Banana (gemini-2.5-flash-image),
        // so it needs no keyed override. Only the video provider differs for
        // this path: image-to-video instead of text-to-video.
        IImageGenerationProvider imageProvider,
        [FromKeyedServices(GoogleFlowKeys.Provider)] IVideoGenerationProvider videoProvider,
        ITtsProvider ttsProvider,
        IGoogleFlowQuotaManager quotaManager,
        IFileStorage fileStorage,
        IAiUsageTracker usageTracker,
        IOptions<PricingOptions> pricing,
        ILogger<GoogleFlowAssetGenerationService> logger)
    {
        _pricing = pricing.Value;
        _projectRepository = projectRepository;
        _storyboardService = storyboardService;
        _storyboardRepository = storyboardRepository;
        _assetService = assetService;
        _hookScriptAgent = hookScriptAgent;
        _sceneGenerator = sceneGenerator;
        _imageProvider = imageProvider;
        _videoProvider = videoProvider;
        _ttsProvider = ttsProvider;
        _quotaManager = quotaManager;
        _fileStorage = fileStorage;
        _usageTracker = usageTracker;
        _logger = logger;
    }

    public async Task RunAsync(
        Guid contentProjectId,
        string topic,
        string audience,
        bool autoHook = false,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        try
        {
            project.TransitionToIfNeeded(ContentProjectStatus.Generating);
            await _projectRepository.SaveChangesAsync(cancellationToken);

            // If the user already built a clip plan, honour it (so the wizard's
            // per-clip editor means something in this mode too). Only fall back
            // to auto-writing a fixed 3-scene hook script when there is no plan.
            var existingStoryboard = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken);
            var usingUserPlan = !autoHook && existingStoryboard is { Scenes.Count: > 0 };

            List<HookScriptScene> flowScenes;
            List<Guid> sceneIds;

            if (usingUserPlan)
            {
                _logger.LogInformation("Google Flow using the user's {Count}-scene clip plan", existingStoryboard!.Scenes.Count);
                var ordered = existingStoryboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
                flowScenes = ordered.Select(ToFlowScene).ToList();
                sceneIds = ordered.Select(s => s.Id).ToList();
            }
            else
            {
                project.ReportProgress(Stage, 0, 4, "Đang viết kịch bản hook 20 giây");
                await _projectRepository.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Step 1: Generating hook-heavy 20s script for {ProjectId}", contentProjectId);
                var hookScript = await _hookScriptAgent.GenerateAsync(topic, audience, cancellationToken);

                if (existingStoryboard is not null)
                {
                    await _storyboardRepository.DeleteScenesByStoryboardIdAsync(existingStoryboard.Id, cancellationToken);
                }
                await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);

                sceneIds = new List<Guid>();
                foreach (var hookScene in hookScript.Scenes)
                {
                    var added = await _storyboardService.AddSceneAsync(
                        contentProjectId,
                        new CreateSceneRequest(
                            hookScene.DurationSeconds,
                            hookScene.Narration,
                            hookScene.VisualDescription,
                            hookScene.MotionDirection,
                            SceneVisualType.AiVideo),
                        cancellationToken);
                    sceneIds.Add(added.Scenes.Last().Id);
                }

                flowScenes = hookScript.Scenes;
            }

            project.ReportProgress(Stage, 1, 4, "Đang tạo ảnh nền");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            // Loaded once and reused for every scene below, exactly like the
            // standard pipeline (SceneAssetGenerator.BuildContextAsync) - same
            // approved Character/Environment anchors and style preset for all
            // 3 scenes, so the free Nano Banana images stay visually consistent
            // instead of each being generated from scratch with no anchor.
            var context = await _sceneGenerator.BuildContextAsync(project, cancellationToken);

            // Step 2: Generate images via Nano Banana (one per scene)
            _logger.LogInformation("Step 2: Generating {SceneCount} images via Nano Banana", flowScenes.Count);
            var images = await GenerateImagesForScenesAsync(contentProjectId, flowScenes, sceneIds, context, cancellationToken);

            project.ReportProgress(Stage, 2, 4, "Đang chuyển ảnh thành video");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            // Step 3: Generate videos from images via Veo Image-to-Video
            _logger.LogInformation("Step 3: Generating {SceneCount} videos from images", flowScenes.Count);
            await GenerateVideosFromImagesAsync(contentProjectId, flowScenes, sceneIds, images, project.AspectRatio, cancellationToken);

            // Step 4: Generate TTS voice-over - but only when the project's audio
            // mode actually wants a generated voice. "Keep original audio" (the
            // default) and "Mute" use the clip's own audio, so TTS here would be
            // wasted spend and would fight the clip's embedded voice.
            if (project.AudioMode == AudioMode.Generated)
            {
                project.ReportProgress(Stage, 3, 4, "Đang lồng tiếng");
                await _projectRepository.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Step 4: Generating TTS narration for {SceneCount} scenes", flowScenes.Count);
                await GenerateVoiceForScenesAsync(contentProjectId, flowScenes, sceneIds, cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "Step 4: Skipping TTS - audio mode is {AudioMode}, the clips' own audio will be used",
                    project.AudioMode);
            }

            project.TransitionToIfNeeded(ContentProjectStatus.Editing);
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("✓ Google Flow asset generation complete for {ProjectId} - 20s video ready for rendering", contentProjectId);
        }
        catch (GoogleFlowQuotaExceededException ex)
        {
            _logger.LogWarning(
                ex,
                "Google Flow quota exceeded: {Credits} credits remaining, {Videos} videos generated today",
                ex.RemainingCredits,
                ex.VideosGenerated);

            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null)
            {
                freshProject.TransitionToIfNeeded(ContentProjectStatus.Failed);
                freshProject.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Flow asset generation failed for {ProjectId}", contentProjectId);

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

    /// <summary>
    /// Adapts a real storyboard scene to the shape the Flow image/video/voice
    /// loops already expect. A clip-plan scene usually only has narration, so
    /// the best available text is used for the image description.
    /// </summary>
    private static HookScriptScene ToFlowScene(Domain.Storyboards.Scene scene)
    {
        var description = FirstNonBlank(scene.GenerationPrompt, scene.VisualDescription, scene.Narration, "a cinematic vertical shot");
        // Prefer the deterministic camera enum; fall back to the legacy free-form note.
        var camera = VideoPromptBuilder.ParseCamera(scene.CameraMovement.ToString());
        if (camera == Domain.Storyboards.CameraMovement.Unspecified)
        {
            camera = VideoPromptBuilder.ParseCamera(scene.CameraDirection);
        }
        var motion = camera != Domain.Storyboards.CameraMovement.Unspecified
            ? VideoPromptBuilder.CameraToText(camera)
            : FirstNonBlank(scene.CameraDirection, "gentle cinematic camera motion");
        return new HookScriptScene(scene.SceneNumber, scene.DurationSeconds, "Scene", description, scene.Narration, 2, motion);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private async Task<List<byte[]>> GenerateImagesForScenesAsync(
        Guid contentProjectId,
        List<Agents.HookScriptScene> hookScenes,
        List<Guid> sceneIds,
        SceneGenerationContext context,
        CancellationToken cancellationToken)
    {
        var images = new List<byte[]>();
        var promptSuffix = string.IsNullOrWhiteSpace(context.Style.VisualStyleGuidance)
            ? string.Empty
            : $"\n\nVisual style: {context.Style.VisualStyleGuidance}.";

        foreach (var (hookScene, sceneId) in hookScenes.Zip(sceneIds))
        {
            _logger.LogDebug("Generating image for Scene {SceneNum}: {Description}",
                hookScene.SceneNumber,
                hookScene.VisualDescription[..Math.Min(50, hookScene.VisualDescription.Length)]);

            var result = await _imageProvider.GenerateAsync(
                new ImageGenerationRequest(
                    Prompt: hookScene.VisualDescription + promptSuffix,
                    NegativePrompt: context.Style.NegativePrompt,
                    ReferenceImages: context.ReferenceImages),
                cancellationToken);

            // Save image
            var imagePath = $"content-projects/{contentProjectId}/scenes/{sceneId}/image.png";
            await _fileStorage.SaveAsync(imagePath, result.ImageBytes, cancellationToken);

            // Store as reference for video generation
            images.Add(result.ImageBytes);

            // Track image cost
            await _usageTracker.RecordAsync(
                new RecordUsageInput(
                    "nano_banana",
                    result.Model,
                    "image_generation",
                    _pricing.ImageUsd,
                    contentProjectId,
                    sceneId),
                cancellationToken);

            _logger.LogInformation("✓ Image {SceneNum} generated (FREE via Nano Banana)", hookScene.SceneNumber);
        }

        return images;
    }

    private async Task GenerateVideosFromImagesAsync(
        Guid contentProjectId,
        List<Agents.HookScriptScene> hookScenes,
        List<Guid> sceneIds,
        List<byte[]> images,
        string aspectRatio,
        CancellationToken cancellationToken)
    {
        foreach (var (hookScene, sceneId, imageBytes) in
                 hookScenes.Zip(sceneIds).Zip(images, (pair, img) => (pair.First, pair.Second, img)))
        {
            // Veo bills real USD per second generated, not a free daily credit
            // pool, so generation is no longer stopped by CheckDailyQuotaAsync -
            // the monthly USD budget (IAiUsageTracker) is the real spend guard.
            _logger.LogDebug("Generating video for Scene {SceneNum} with motion level {MotionLevel}",
                hookScene.SceneNumber,
                hookScene.MotionLevel);

            // Build motion prompt with detailed instructions for the video generation model
            var motionPrompt = $"""
{hookScene.VisualDescription}

MOTION DIRECTION: {hookScene.MotionDirection}
MOTION INTENSITY: Level {hookScene.MotionLevel} (1=subtle, 3=intense)

Animate this image accordingly. Focus on smooth, natural motion that enhances the narrative without being distracting.
""";

            var referenceImage = new ReferenceImage(imageBytes, "image/png");
            var result = await _videoProvider.GenerateAsync(
                new VideoGenerationRequest(
                    Prompt: motionPrompt,
                    NegativePrompt: "static, no movement, frozen",
                    DurationSeconds: hookScene.DurationSeconds,
                    ReferenceImages: new[] { referenceImage },
                    AspectRatio: aspectRatio),
                cancellationToken);

            // Versioned filename + supersede so re-running over the same clip
            // plan replaces the visual instead of piling up.
            var videoPath = await _fileStorage.SaveAsync(
                $"content-projects/{contentProjectId}/scenes/{sceneId}/visual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.mp4",
                result.VideoBytes,
                cancellationToken);

            await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Video, cancellationToken);
            await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Image, cancellationToken);

            await _assetService.CreateAsync(
                contentProjectId,
                new CreateAssetRequest(
                    sceneId,
                    AssetType.Video,
                    result.Model,
                    motionPrompt,
                    videoPath,
                    hookScene.DurationSeconds,
                    null,
                    null),
                cancellationToken);

            // Record quota usage
            await _quotaManager.RecordVideoGenerationAsync(contentProjectId, sceneId, cancellationToken);

            // Track video cost
            await _usageTracker.RecordAsync(
                new RecordUsageInput(
                    "veo",
                    result.Model,
                    "video_generation",
                    _pricing.VideoUsdPerSecond * hookScene.DurationSeconds,
                    contentProjectId,
                    sceneId),
                cancellationToken);

            _logger.LogInformation(
                "✓ Video {SceneNum} generated ({Duration}s, Motion Level {Level}, ~10 credits)",
                hookScene.SceneNumber,
                hookScene.DurationSeconds,
                hookScene.MotionLevel);
        }
    }

    private async Task GenerateVoiceForScenesAsync(
        Guid contentProjectId,
        List<Agents.HookScriptScene> hookScenes,
        List<Guid> sceneIds,
        CancellationToken cancellationToken)
    {
        foreach (var (hookScene, sceneId) in hookScenes.Zip(sceneIds))
        {
            if (string.IsNullOrWhiteSpace(hookScene.Narration))
            {
                _logger.LogWarning("Scene {SceneNum} has empty narration, skipping TTS", hookScene.SceneNumber);
                continue;
            }

            var result = await _ttsProvider.GenerateAsync(
                new TtsRequest(hookScene.Narration, VoiceName: null),
                cancellationToken);

            var voicePath = await _fileStorage.SaveAsync(
                $"content-projects/{contentProjectId}/scenes/{sceneId}/voice-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.wav",
                result.AudioBytes,
                cancellationToken);

            await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Voice, cancellationToken);

            await _assetService.CreateAsync(
                contentProjectId,
                new CreateAssetRequest(
                    sceneId,
                    AssetType.Voice,
                    result.Model,
                    hookScene.Narration,
                    voicePath,
                    result.DurationSeconds,
                    null,
                    null),
                cancellationToken);

            // Track TTS cost
            var ttsCost = result.IsFree ? 0m : _pricing.TtsUsdPer1000Chars * hookScene.Narration.Length / 1000m;
            await _usageTracker.RecordAsync(
                new RecordUsageInput(
                    "gemini",
                    result.Model,
                    "tts_generation",
                    ttsCost,
                    contentProjectId,
                    sceneId),
                cancellationToken);

            _logger.LogInformation(
                "✓ Voice {SceneNum} generated ({Words} words, ~${Cost:F4})",
                hookScene.SceneNumber,
                hookScene.Narration.Split(' ').Length,
                ttsCost);
        }
    }
}
