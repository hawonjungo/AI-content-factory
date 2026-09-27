using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// Step 5's two-stage Keyframe -&gt; Video workflow: generate and approve one
/// still image per scene (Stage 1, <see cref="GenerateKeyframeAsync"/>), then
/// animate that APPROVED image into a video via the standard Veo
/// image-to-video call (Stage 2, <see cref="GenerateVideoFromKeyframeAsync"/>),
/// at the scene's own configured duration (not a hardcoded length).
/// Both stages are real, billable provider calls that go through the exact
/// same <see cref="IAssetService"/>/<see cref="IFileStorage"/>/
/// <see cref="IAiUsageTracker"/> plumbing <see cref="SceneAssetGenerator"/>
/// already uses for the direct-to-video path - a Keyframe IS an ordinary
/// <see cref="AssetType.Image"/> Asset, just one Step 5 lets the user review
/// and approve before it is animated, tracked via <see cref="Scene.KeyframeStatus"/>
/// (a field wholly separate from <see cref="Scene.Status"/>, which continues
/// to mean exactly what it always has for the scene's own final visual).
///
/// This is entirely optional: a scene that never calls either method here
/// keeps <see cref="KeyframeStatus.None"/> forever and every existing
/// generation path (<see cref="SceneAssetGenerator"/>, the Google Flow
/// copy-paste export) is completely unaffected.
/// </summary>
public interface ISceneKeyframeService
{
    /// <summary>
    /// Generates (or regenerates) this scene's Keyframe still image. Throws
    /// <see cref="DomainException"/> if the project is busy with another
    /// billable operation, one is already generating, or the scene has no
    /// vetted visual action text to compose a prompt from (the same "never
    /// fall back to raw narration/dialogue" rule the Flow export applies).
    /// </summary>
    Task<SceneResponse> GenerateKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Animates the scene's APPROVED Keyframe into a video via the standard
    /// Veo image-to-video call, using <see cref="Scene.MotionPrompt"/> (or a
    /// composed default). Throws <see cref="DomainException"/> if the project
    /// is busy with another billable operation, the Keyframe isn't approved
    /// yet, the scene is already generating, or there's no motion prompt and
    /// nothing to compose a default one from.
    /// </summary>
    Task<SceneResponse> GenerateVideoFromKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);
}

public class SceneKeyframeService : ISceneKeyframeService
{
    /// <summary>Project-busy-lock stage keys - see the reservation note in each method below.</summary>
    public const string KeyframeStage = "scene-keyframe";
    public const string KeyframeVideoStage = "scene-keyframe-video";

    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IContentProjectRepository _projectRepository;
    private readonly IProjectJobReservationService _jobReservation;
    private readonly ISceneAssetGenerator _sceneAssetGenerator;
    private readonly Stories.IStoryVisualContextResolver _storyVisualContextResolver;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly IVideoGenerationProvider _videoProvider;
    private readonly IAssetService _assetService;
    private readonly IFileStorage _fileStorage;
    private readonly IAiUsageTracker _usageTracker;
    private readonly ICreditLedger _creditLedger;
    private readonly PricingOptions _pricing;
    private readonly CreditCostOptions _creditCosts;
    private readonly ILogger<SceneKeyframeService> _logger;

    public SceneKeyframeService(
        IStoryboardRepository storyboardRepository,
        IContentProjectRepository projectRepository,
        IProjectJobReservationService jobReservation,
        ISceneAssetGenerator sceneAssetGenerator,
        Stories.IStoryVisualContextResolver storyVisualContextResolver,
        IImageGenerationProvider imageProvider,
        IVideoGenerationProvider videoProvider,
        IAssetService assetService,
        IFileStorage fileStorage,
        IAiUsageTracker usageTracker,
        ICreditLedger creditLedger,
        IOptions<PricingOptions> pricing,
        IOptions<CreditCostOptions> creditCosts,
        ILogger<SceneKeyframeService> logger)
    {
        _storyboardRepository = storyboardRepository;
        _projectRepository = projectRepository;
        _jobReservation = jobReservation;
        _sceneAssetGenerator = sceneAssetGenerator;
        _storyVisualContextResolver = storyVisualContextResolver;
        _imageProvider = imageProvider;
        _videoProvider = videoProvider;
        _assetService = assetService;
        _fileStorage = fileStorage;
        _usageTracker = usageTracker;
        _creditLedger = creditLedger;
        _pricing = pricing.Value;
        _creditCosts = creditCosts.Value;
        _logger = logger;
    }

    public async Task<SceneResponse> GenerateKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        // Both stages here run synchronously in the HTTP request (unlike
        // Hangfire-queued jobs), each making a real billable provider call, so
        // the same project-wide busy-lock every other billable action in this
        // app goes through (IProjectJobReservationService - script/asset-gen/
        // render/QA/bulk-prompt-suggest) applies here too: a double-click,
        // client retry, or second browser tab can no longer start a second
        // paid call while one is in flight for this project. Cleared in the
        // outer `finally` below since there is no background job to clear it.
        var project = await _jobReservation.ReserveAsync(contentProjectId, KeyframeStage, "Đang tạo Keyframe", cancellationToken)
            ?? throw new DomainException("Dự án đang bận với một thao tác khác - vui lòng đợi rồi thử lại.");

        try
        {
            var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
                ?? throw new DomainException("Storyboard not found for this content project.");
            var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
                ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

            // The same "never dress up dialogue as a visual description" rule the
            // Flow export applies (FlowGenerationPlanService.IsUnprompted) -
            // Narration is the TTS/voiceover script, not a visual prompt.
            var action = FirstNonBlank(scene.GenerationPrompt, scene.VisualDescription)
                ?? throw new DomainException("This scene has no visual description or generated prompt yet - write one (or run 'Gợi ý prompt') before generating a Keyframe.");

            // Throws if already generating - defense in depth alongside the
            // project-wide lock above (e.g. a prior request that crashed
            // before clearing its reservation).
            scene.MarkKeyframeGenerating();
            await _storyboardRepository.SaveChangesAsync(cancellationToken);

            try
            {
                var style = PresetCatalog.ResolveStyle(project.StylePresetId);
                var context = await _sceneAssetGenerator.BuildContextAsync(project, cancellationToken);
                var visualDescriptions = await _storyVisualContextResolver.GetCastAndLocationVisualDescriptionsAsync(contentProjectId, cancellationToken);
                var behaviorProfiles = await _storyVisualContextResolver.GetCharacterBehaviorProfilesAsync(contentProjectId, cancellationToken);

                IReadOnlyList<ApprovedSceneReference> matched = ReferenceMatcher.Match(
                    context.ApprovedReferences, scene.RelevantReferenceLabels, scene.Narration,
                    typeOf: r => r.Type, labelOf: r => r.Image.Label);

                // Same rule as the Flow export and the direct Veo path: a shot
                // judged to show no recurring character never gets one.
                var characterOffScreen = scene.CharacterOnScreen == false;
                if (characterOffScreen)
                {
                    matched = matched.Where(r => r.Type != Domain.AssetReferences.AssetReferenceType.Character).ToList();
                }

                var selectedReferences = matched.Count > 0
                    ? matched.Select(r => r.Image).ToList()
                    : characterOffScreen
                        ? context.ReferenceImages.Where(image => !ReferenceEquals(image, context.CharacterReference)).ToList()
                        : context.ReferenceImages;
                var (characterLabels, _) = ReferenceMatcher.SplitNames(matched, r => r.Type, r => r.Image.Label);

                var prompt = ImagePromptComposer.Compose(
                    action, style.VisualStyleGuidance, context.CharacterReference is not null && !characterOffScreen,
                    characterLabels, visualDescriptions, behaviorProfiles, scene.ShotSize);

                await _usageTracker.EnsureBudgetAvailableAsync(cancellationToken);
                await _creditLedger.EnsureAvailableAsync(_creditCosts.ImageCredits, cancellationToken);

                var result = await _imageProvider.GenerateAsync(
                    new ImageGenerationRequest(prompt, style.NegativePrompt, selectedReferences),
                    cancellationToken);

                var extension = result.MimeType.Contains("png") ? "png" : "jpg";
                var storedPath = await _fileStorage.SaveAsync(
                    $"content-projects/{contentProjectId}/scenes/{sceneId}/keyframe-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}",
                    result.ImageBytes,
                    cancellationToken);

                // A scene has at most one current Ready visual asset - the render
                // pipeline picks whichever of Video/Image is newest (see
                // RenderService), with no awareness of "Keyframe" as a distinct,
                // still-under-review concept. Retiring BOTH kinds here (the same
                // "switches type over its life" rule SceneAssetGenerator and
                // AssetService.UploadSceneVideoAsync already apply) prevents a
                // fresh, unapproved Keyframe from silently outranking a finished,
                // good video if the project is rendered mid-review.
                await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Video, cancellationToken);
                await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Image, cancellationToken);
                var asset = await _assetService.CreateAsync(
                    contentProjectId,
                    new CreateAssetRequest(sceneId, AssetType.Image, result.Model, prompt, storedPath, null, null, null),
                    cancellationToken);

                scene.MarkKeyframeGenerated(asset.Id, prompt);
                await _storyboardRepository.SaveChangesAsync(cancellationToken);

                await _usageTracker.RecordAsync(
                    new RecordUsageInput("gemini", result.Model, "scene_keyframe_generation", _pricing.ImageUsd, contentProjectId, sceneId),
                    cancellationToken);

                _logger.LogInformation("Keyframe generated for Scene {SceneId} (ContentProject {ContentProjectId})", sceneId, contentProjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keyframe generation failed for Scene {SceneId} (ContentProject {ContentProjectId})", sceneId, contentProjectId);
                scene.MarkKeyframeFailed();
                await _storyboardRepository.SaveChangesAsync(cancellationToken);
                throw;
            }

            var fresh = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
                ?? throw new DomainException("Storyboard was not found after Keyframe generation.");
            return StoryboardResponse.FromDomain(fresh).Scenes.First(s => s.Id == sceneId);
        }
        finally
        {
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<SceneResponse> GenerateVideoFromKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        // See the identical reservation note in GenerateKeyframeAsync above.
        var project = await _jobReservation.ReserveAsync(contentProjectId, KeyframeVideoStage, "Đang tạo video từ Keyframe", cancellationToken)
            ?? throw new DomainException("Dự án đang bận với một thao tác khác - vui lòng đợi rồi thử lại.");

        try
        {
            var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
                ?? throw new DomainException("Storyboard not found for this content project.");
            var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
                ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

            if (scene.KeyframeStatus != KeyframeStatus.Approved || scene.KeyframeAssetId is not { } keyframeAssetId)
            {
                throw new DomainException("This scene's Keyframe must be approved before generating a video from it.");
            }

            if (scene.Status == SceneStatus.Generating)
            {
                throw new DomainException("This scene is already generating.");
            }

            // A hand-written motion prompt wins; otherwise the motion-only
            // prompt the Flow export also shows (the Keyframe already fixes
            // appearance/setting/style, so only movement is described).
            var action = FirstNonBlank(scene.GenerationPrompt, scene.VisualDescription);
            var motionPrompt = FirstNonBlank(scene.MotionPrompt)
                ?? (action is null
                    ? null
                    : VideoPromptBuilder.BuildMotion(new VideoPromptSpec(
                        Action: action,
                        Camera: scene.CameraMovement,
                        HasCharacterReference: false,
                        HasEnvironmentReference: false,
                        StyleGuidance: null,
                        DurationSeconds: scene.DurationSeconds,
                        AspectRatio: project.AspectRatio,
                        Shot: scene.ShotSize)))
                ?? throw new DomainException("No motion prompt is available - set one first or write a visual description.");

            var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
            var keyframeAsset = assets.FirstOrDefault(a => a.Id == keyframeAssetId)
                ?? throw new DomainException("The approved Keyframe's image could not be found - it may have been deleted. Generate a new one.");
            if (string.IsNullOrWhiteSpace(keyframeAsset.FilePath))
            {
                throw new DomainException("The approved Keyframe has no stored image file. Generate a new one.");
            }

            scene.MarkGenerating();
            await _storyboardRepository.SaveChangesAsync(cancellationToken);

            try
            {
                await using var keyframeStream = await _fileStorage.GetAsync(keyframeAsset.FilePath, cancellationToken);
                using var buffer = new MemoryStream();
                await keyframeStream.CopyToAsync(buffer, cancellationToken);
                var keyframeBytes = buffer.ToArray();
                var keyframeMimeType = keyframeAsset.FilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

                var tier = Enum.TryParse<Domain.Generation.VideoModelTier>(scene.ModelTier, ignoreCase: true, out var t)
                    ? t
                    : Domain.Generation.VideoModelTier.Lite;

                await _usageTracker.EnsureBudgetAvailableAsync(cancellationToken);
                await _creditLedger.EnsureAvailableAsync(_creditCosts.VideoCreditsFor(tier), cancellationToken);

                var initialImage = new ReferenceImage(keyframeBytes, keyframeMimeType, "Keyframe", motionPrompt);
                var result = await _videoProvider.GenerateAsync(
                    new VideoGenerationRequest(motionPrompt, NegativePrompt: null, scene.DurationSeconds, AspectRatio: project.AspectRatio, InitialImage: initialImage),
                    cancellationToken);

                var storedPath = await _fileStorage.SaveAsync(
                    $"content-projects/{contentProjectId}/scenes/{sceneId}/visual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.mp4",
                    result.VideoBytes,
                    cancellationToken);

                await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Video, cancellationToken);
                await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Image, cancellationToken);

                await _assetService.CreateAsync(
                    contentProjectId,
                    new CreateAssetRequest(sceneId, AssetType.Video, result.Model, motionPrompt, storedPath, scene.DurationSeconds, null, null),
                    cancellationToken);

                scene.MarkGenerated();
                await _storyboardRepository.SaveChangesAsync(cancellationToken);

                await _usageTracker.RecordAsync(
                    new RecordUsageInput("gemini", result.Model, "keyframe_video_generation", _pricing.VideoUsdPerSecondFor(tier) * scene.DurationSeconds, contentProjectId, sceneId),
                    cancellationToken);

                _logger.LogInformation("Video generated from Keyframe for Scene {SceneId} (ContentProject {ContentProjectId})", sceneId, contentProjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keyframe-to-video generation failed for Scene {SceneId} (ContentProject {ContentProjectId})", sceneId, contentProjectId);
                scene.MarkFailed();
                await _storyboardRepository.SaveChangesAsync(cancellationToken);
                throw;
            }

            var fresh = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
                ?? throw new DomainException("Storyboard was not found after video generation.");
            return StoryboardResponse.FromDomain(fresh).Scenes.First(s => s.Id == sceneId);
        }
        finally
        {
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
