using AiContentFactory.Application.Agents;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Application.Tts;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// One approved reference image paired with its <see cref="AssetReference.Type"/>
/// - carried alongside the provider-facing <see cref="ReferenceImage"/> so
/// per-scene selection (see <see cref="SceneAssetGenerator.SelectReferencesForScene"/>)
/// can apply the "Environment/location first" priority rule without
/// <see cref="AssetReferenceType"/> leaking into the provider abstraction
/// itself (VeoVideoProvider only ever sees the plain <see cref="Image"/>).
/// </summary>
public sealed record ApprovedSceneReference(AssetReferenceType Type, ReferenceImage Image);

/// <summary>
/// The result of <see cref="SceneAssetGenerator.SelectReferencesForScene"/>:
/// the actual reference images to send to the provider, plus - when the
/// per-scene tag match found named rows - the real names behind them, so
/// <see cref="VideoPromptBuilder.Build"/> can name the character(s) and
/// location in the prompt text instead of only attaching their image bytes.
/// <see cref="CharacterLabels"/> empty / <see cref="LocationLabel"/> null
/// means nothing scene-specific matched (a non-Story project, or a Story
/// scene whose tags found no approved reference) - the classic
/// Character/Environment pair is still used for <see cref="Images"/>, it
/// just isn't a *named* one, so the prompt falls back to today's generic
/// consistency sentences.
/// </summary>
public sealed record SceneReferenceSelection(
    IReadOnlyList<ReferenceImage> Images,
    IReadOnlyList<string> CharacterLabels,
    string? LocationLabel);

/// <summary>
/// Everything needed to generate one scene, resolved once per run so a
/// 5-clip project doesn't re-read reference images or re-resolve presets five
/// times.
/// </summary>
public record SceneGenerationContext(
    Guid ContentProjectId,
    string AspectRatio,
    StylePreset Style,
    VoiceProfile Voice,
    ReferenceImage? CharacterReference,
    ReferenceImage? EnvironmentReference,
    IReadOnlyList<ApprovedSceneReference> ApprovedReferences,
    IReadOnlyDictionary<string, CharacterBehaviorProfile>? CharacterBehaviorProfiles = null)
{
    /// <summary>
    /// The classic Character+Environment pair - a slot the user skipped
    /// contributes nothing. This is the legacy/fallback selection used
    /// whenever a scene has no per-scene tagged references (see
    /// <see cref="SceneAssetGenerator.SelectReferencesForScene"/>), and still
    /// drives the prompt's "Character:"/"Environment:" lines via
    /// <see cref="CharacterReference"/>/<see cref="EnvironmentReference"/>.
    /// </summary>
    public IReadOnlyList<ReferenceImage> ReferenceImages =>
        new[] { CharacterReference, EnvironmentReference }.Where(r => r is not null).Select(r => r!).ToArray();
}

public interface ISceneAssetGenerator
{
    Task<SceneGenerationContext> BuildContextAsync(ContentProject project, CancellationToken cancellationToken = default);

    /// <param name="refreshPrompt">
    /// Discard any prompt already stored on the scene and write a new one.
    /// Used when regenerating a single clip - reusing the identical prompt
    /// would mostly reproduce the clip the user just rejected.
    /// </param>
    Task GenerateClipAsync(SceneGenerationContext context, SceneResponse scene, bool refreshPrompt, CancellationToken cancellationToken = default);

    Task GenerateVoiceAsync(SceneGenerationContext context, SceneResponse scene, CancellationToken cancellationToken = default);
}

/// <summary>
/// The single code path for turning one storyboard scene into a Veo clip plus
/// its TTS voice-over. Both the full-project run (AssetGenerationService) and
/// the single-clip rerun (ClipRegenerationService) go through here, so the
/// two can't drift apart the way AssetGenerationService and
/// GoogleFlowAssetGenerationService already have.
/// </summary>
public class SceneAssetGenerator : ISceneAssetGenerator
{
    private readonly IStoryboardService _storyboardService;
    private readonly IAssetService _assetService;
    private readonly IPromptAgent _promptAgent;
    private readonly IVideoGenerationProvider _videoProvider;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly ITtsService _ttsService;
    private readonly IAudioTimingService _audioTiming;
    private readonly IAssetReferenceService _assetReferenceService;
    private readonly IGoogleFlowQuotaManager _quotaManager;
    private readonly ICreditLedger _creditLedger;
    private readonly IFileStorage _fileStorage;
    private readonly IAiUsageTracker _usageTracker;
    private readonly IStoryVisualContextResolver _storyVisualContextResolver;
    private readonly PricingOptions _pricing;
    private readonly CreditCostOptions _creditCosts;
    private readonly GoogleFlowOptions _flow;
    private readonly VideoGenerationOptions _videoOptions;
    private readonly ILogger<SceneAssetGenerator> _logger;

    public SceneAssetGenerator(
        IStoryboardService storyboardService,
        IAssetService assetService,
        IPromptAgent promptAgent,
        IVideoGenerationProvider videoProvider,
        IImageGenerationProvider imageProvider,
        ITtsService ttsService,
        IAudioTimingService audioTiming,
        IAssetReferenceService assetReferenceService,
        IGoogleFlowQuotaManager quotaManager,
        ICreditLedger creditLedger,
        IFileStorage fileStorage,
        IAiUsageTracker usageTracker,
        IStoryVisualContextResolver storyVisualContextResolver,
        IOptions<PricingOptions> pricing,
        IOptions<CreditCostOptions> creditCosts,
        IOptions<GoogleFlowOptions> flow,
        IOptions<VideoGenerationOptions> videoOptions,
        ILogger<SceneAssetGenerator> logger)
    {
        _storyboardService = storyboardService;
        _assetService = assetService;
        _promptAgent = promptAgent;
        _videoProvider = videoProvider;
        _imageProvider = imageProvider;
        _ttsService = ttsService;
        _audioTiming = audioTiming;
        _assetReferenceService = assetReferenceService;
        _quotaManager = quotaManager;
        _creditLedger = creditLedger;
        _fileStorage = fileStorage;
        _usageTracker = usageTracker;
        _storyVisualContextResolver = storyVisualContextResolver;
        _pricing = pricing.Value;
        _creditCosts = creditCosts.Value;
        _flow = flow.Value;
        _videoOptions = videoOptions.Value;
        _logger = logger;
    }

    private static SceneVisualType ParseVisualType(string value) =>
        Enum.TryParse<SceneVisualType>(value, ignoreCase: true, out var parsed) ? parsed : SceneVisualType.AiVideo;

    public async Task<SceneGenerationContext> BuildContextAsync(ContentProject project, CancellationToken cancellationToken = default)
    {
        var referenceImages = await LoadReferenceImagesAsync(project.Id, cancellationToken);

        // A slot the user chose to skip contributes no anchor - that's allowed
        // (approve-or-skip). Consistency for a missing anchor falls back to the
        // text prompt. Only matches the classic null-Label rows (their
        // ReferenceImage.Label falls back to the Type string "Character"/
        // "Environment" - see AssetReferenceService.LoadApprovedImagesAsync) -
        // a named row's Label is its actual name, so it never accidentally
        // satisfies this legacy pair.
        var characterReference = referenceImages.FirstOrDefault(image =>
            string.Equals(image.Label, "Character", StringComparison.OrdinalIgnoreCase));
        var environmentReference = referenceImages.FirstOrDefault(image =>
            string.Equals(image.Label, "Environment", StringComparison.OrdinalIgnoreCase));

        // Resolved once per run, same as referenceImages above: a Label ->
        // Type lookup (metadata only, no extra image bytes) so per-scene
        // selection can tell a matched Character reference from a matched
        // Environment/location one for the priority/cap rule.
        var namedReferences = await _assetReferenceService.GetNamedReferencesAsync(project.Id, cancellationToken);
        var typeByLabel = namedReferences
            .Where(r => !string.IsNullOrWhiteSpace(r.Label))
            .GroupBy(r => r.Label!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => Enum.TryParse<AssetReferenceType>(g.First().Type, ignoreCase: true, out var t) ? t : AssetReferenceType.Character,
                StringComparer.OrdinalIgnoreCase);

        var approvedReferences = referenceImages
            .Select(image => new ApprovedSceneReference(ResolveReferenceType(image.Label, typeByLabel), image))
            .ToList();

        var idea = project.IdeaConfig;
        var voicePreset = PresetCatalog.ResolveVoice(project.VoicePresetId, idea.VoiceGender);
        var voiceProfile = VoiceProfile.FromPreset(
            voicePreset,
            idea.VoiceGender,
            idea.SpeakingRate,
            // A free (English-only) voice needs to know the language even when
            // none was typed, so it can refuse a Vietnamese script clearly.
            language: idea.NarrationLanguage ?? (voicePreset.IsFree ? project.Language : null));
        if (!string.IsNullOrWhiteSpace(idea.VoiceStyle))
        {
            voiceProfile = voiceProfile with { StyleInstruction = idea.VoiceStyle };
        }

        // Name -> opted-in CharacterBehaviorProfile lookup from the Story
        // "bible", resolved once per run same as everything else here. Null
        // for every non-Story project (the normal/default case) and for any
        // Story project where no character opted in.
        var characterBehaviorProfiles = await _storyVisualContextResolver.GetCharacterBehaviorProfilesAsync(project.Id, cancellationToken);

        return new SceneGenerationContext(
            project.Id,
            project.AspectRatio,
            PresetCatalog.ResolveStyle(project.StylePresetId),
            voiceProfile,
            characterReference,
            environmentReference,
            approvedReferences,
            characterBehaviorProfiles);
    }

    /// <summary>
    /// A legacy fallback row's <see cref="ReferenceImage.Label"/> IS its Type
    /// string ("Character"/"Environment" - see
    /// AssetReferenceService.LoadApprovedImagesAsync), so it parses directly.
    /// A named row's Label is its real name instead, so its Type comes from
    /// <paramref name="typeByLabel"/> (built from <c>GetNamedReferencesAsync</c>).
    /// Character is the safe default on the rare case neither resolves - it
    /// only affects priority ordering when more than 3 references match one
    /// scene, never whether a reference is selected at all.
    /// </summary>
    private static AssetReferenceType ResolveReferenceType(string? label, IReadOnlyDictionary<string, AssetReferenceType> typeByLabel)
    {
        if (label is not null && Enum.TryParse<AssetReferenceType>(label, ignoreCase: true, out var direct))
        {
            return direct;
        }

        return label is not null && typeByLabel.TryGetValue(label, out var named) ? named : AssetReferenceType.Character;
    }

    public async Task GenerateClipAsync(SceneGenerationContext context, SceneResponse scene, bool refreshPrompt, CancellationToken cancellationToken = default)
    {
        // The backend is the final authority on "skip generation". A scene the
        // user already has a clip for (imported .mp4) or explicitly flagged must
        // never reach a provider, a credit check or a generation job here -
        // whatever the caller or the UI asked for. This is the single chokepoint
        // both the full run and the single-clip rerun go through.
        if (scene.SkipGeneration)
        {
            _logger.LogInformation(
                "Scene {SceneId} is flagged skip-generation - not calling the video/image provider", scene.Id);
            return;
        }

        var visualType = ParseVisualType(scene.VisualType);

        // Only AiVideo (Veo) and AiImage actually have a generation path today.
        // The other SceneVisualType values (ExistingFootage, MotionGraphic,
        // TextAnimation, Diagram) are UI-selectable stubs with no implementation
        // behind them - without this guard they'd silently fall through to
        // GenerateVideoAsync below and make a real, billable Veo call for a
        // feature that doesn't exist yet. Fail loudly, before any budget/credit
        // check or provider call, instead of spending real money on nothing.
        if (visualType != SceneVisualType.AiImage && visualType != SceneVisualType.AiVideo)
        {
            _logger.LogWarning(
                "Scene {SceneId} requested unsupported visual type {VisualType} - no generator is implemented for it yet",
                scene.Id, visualType);
            throw new NotSupportedException(
                $"Scene {scene.Id} requested visual type '{visualType}', which has no generation implementation yet. " +
                "Only AiVideo and AiImage are currently supported.");
        }

        var (action, camera, negativePrompt) = await ResolvePromptAsync(context, scene, visualType, refreshPrompt, cancellationToken);

        // Budget/credit checks happen immediately before each spend, not once
        // per run - a long run can cross a limit partway through.
        await _usageTracker.EnsureBudgetAvailableAsync(cancellationToken);

        // Centralized daily credit budget. The tier-aware reserve/commit cycle
        // lives in FlowGenerationService; here we make the conservative check
        // that at least one more clip's worth of credits is available before
        // calling the provider, so a run can't overshoot the daily pool.
        var requiredCredits = visualType == SceneVisualType.AiImage
            ? _creditCosts.ImageCredits
            : _creditCosts.LiteVideoCredits;
        await _creditLedger.EnsureAvailableAsync(requiredCredits, cancellationToken);

        // Per-scene reference selection: a Story-linked scene tagged with
        // specific names only gets those references, so it doesn't waste one
        // of Veo's 3 reference-image slots on an anchor it doesn't need - and
        // carries the real names along so the prompt text can name them too.
        var selection = SelectReferencesForScene(context, scene);

        if (visualType == SceneVisualType.AiImage)
        {
            await GenerateStillAsync(context, scene, action, negativePrompt, selection.Images, cancellationToken);
        }
        else
        {
            await GenerateVideoAsync(context, scene, action, camera, negativePrompt, selection, cancellationToken);
        }
    }

    /// <summary>
    /// Picks which approved references this scene's generation call actually
    /// gets. A scene tagged via <see cref="Scene.RelevantReferenceLabels"/>
    /// (deterministic name matching done once in ClipPlanService, for
    /// Story-linked projects) gets ONLY the references for the names it
    /// mentions - spanning both Character and Environment rows naturally,
    /// since matching is purely by Label - via the shared
    /// <see cref="ReferenceMatcher"/> (also used by
    /// <see cref="FlowGenerationPlanService"/>, so the matching/cap/priority
    /// rule only exists once). Falls back to the classic single
    /// Character+Environment pair - <see cref="SceneGenerationContext.ReferenceImages"/>,
    /// unchanged from before this feature existed - whenever there's nothing
    /// scene-specific to narrow to: a non-Story project (no tags at all), a
    /// Story scene where name matching found nothing, or where the tagged
    /// names have no approved reference yet. In the fallback case no names
    /// are known, so <see cref="SceneReferenceSelection.CharacterLabels"/> is
    /// empty and <see cref="SceneReferenceSelection.LocationLabel"/> is null.
    /// </summary>
    public static SceneReferenceSelection SelectReferencesForScene(SceneGenerationContext context, SceneResponse scene)
    {
        IReadOnlyList<ApprovedSceneReference> matched = ReferenceMatcher.Match(
            context.ApprovedReferences, scene.RelevantReferenceLabels, scene.Narration,
            typeOf: r => r.Type, labelOf: r => r.Image.Label);

        // The shot was judged to show no recurring character: never send a
        // character image (it would pull the character into e.g. a landscape
        // or a shot of other animals), same rule as the Flow export.
        var characterOffScreen = scene.CharacterOnScreen == false;
        if (characterOffScreen)
        {
            matched = matched.Where(r => r.Type != AssetReferenceType.Character).ToList();
        }

        if (matched.Count > 0)
        {
            var (characterLabels, locationLabel) = ReferenceMatcher.SplitNames(matched, r => r.Type, r => r.Image.Label);
            return new SceneReferenceSelection(matched.Select(r => r.Image).ToList(), characterLabels, locationLabel);
        }

        var fallback = characterOffScreen
            ? context.ReferenceImages.Where(image => !ReferenceEquals(image, context.CharacterReference)).ToList()
            : context.ReferenceImages;
        return new SceneReferenceSelection(fallback, Array.Empty<string>(), null);
    }

    /// <summary>
    /// Resolves the structured shot data for a scene: the primary action, the
    /// deterministic camera, and any negative prompt. Prefers what's already
    /// stored on the scene; only calls the prompt agent when there's no action
    /// yet (or the caller forced a refresh), and persists what the agent picks.
    /// </summary>
    private async Task<(string Action, CameraMovement Camera, string? Negative)> ResolvePromptAsync(
        SceneGenerationContext context,
        SceneResponse scene,
        SceneVisualType visualType,
        bool refreshPrompt,
        CancellationToken cancellationToken)
    {
        var storedAction = refreshPrompt ? null : scene.GenerationPrompt;
        var negativePrompt = refreshPrompt ? null : scene.NegativePrompt;

        var storedCamera = VideoPromptBuilder.ParseCamera(scene.CameraMovement);
        if (storedCamera == CameraMovement.Unspecified)
        {
            storedCamera = VideoPromptBuilder.ParseCamera(scene.CameraDirection);
        }

        if (!string.IsNullOrWhiteSpace(storedAction))
        {
            return (storedAction!, storedCamera, negativePrompt);
        }

        var storyVisualContext = await _storyVisualContextResolver.ResolveAsync(context.ContentProjectId, cancellationToken);

        var promptOutput = await _promptAgent.GenerateAsync(
            new PromptAgentInput(
                scene.Narration,
                scene.VisualDescription,
                scene.CameraDirection,
                visualType,
                context.Style.VisualStyleGuidance,
                context.Style.NegativePrompt,
                storyVisualContext),
            cancellationToken);

        var camera = promptOutput.Camera != CameraMovement.Unspecified ? promptOutput.Camera : storedCamera;

        await _storyboardService.SetScenePromptAsync(
            context.ContentProjectId, scene.Id, promptOutput.Action, promptOutput.NegativePrompt, promptOutput.VisualStyle, "gemini", cancellationToken);
        if (camera != CameraMovement.Unspecified)
        {
            await _storyboardService.SetSceneCameraMovementAsync(context.ContentProjectId, scene.Id, camera, cancellationToken);
        }

        return (promptOutput.Action, camera, promptOutput.NegativePrompt);
    }

    private async Task GenerateVideoAsync(SceneGenerationContext context, SceneResponse scene, string action, CameraMovement camera, string? negativePrompt, SceneReferenceSelection selection, CancellationToken cancellationToken)
    {
        // Deterministic structured prompt: Action / Setting / Character /
        // Environment / Camera / Style / Format / Restrictions, with the
        // reference lines included only when that anchor actually exists -
        // named when the per-scene selection resolved real names.
        var anchoredPrompt = BuildClipPrompt(context, scene, action, camera, selection);
        var selectedReferences = selection.Images;

        VideoGenerationResult result;
        string anchorModel = "none";

        if (_videoOptions.ImageToVideoEnabled)
        {
            // True image-to-video: render one anchor frame from the approved
            // references, then animate it - identity, location, palette and
            // lighting fixed before Veo produces any motion.
            var anchor = await _imageProvider.GenerateAsync(
                new ImageGenerationRequest(anchoredPrompt, negativePrompt, selectedReferences),
                cancellationToken);
            anchorModel = anchor.Model;
            var initialFrame = new ReferenceImage(anchor.ImageBytes, anchor.MimeType, "SceneAnchor", anchoredPrompt);

            result = await _videoProvider.GenerateAsync(
                new VideoGenerationRequest(anchoredPrompt, negativePrompt, scene.DurationSeconds, AspectRatio: context.AspectRatio, InitialImage: initialFrame),
                cancellationToken);
        }
        else
        {
            // No anchor frame is rendered here (that costs an extra image
            // call), but the scene's selected references (per-scene narrowed
            // for a tagged Story scene, otherwise the classic Character/
            // Environment pair) are still offered as Veo's native
            // referenceImages (up to 3) for real visual grounding instead of
            // relying on anchoredPrompt's wording alone. Models that reject
            // inline images (veo-3.1-fast) 400 on this; VeoVideoProvider
            // detects that specific rejection and automatically retries as
            // pure text-to-video, so this is safe regardless of which model
            // is configured.
            result = await _videoProvider.GenerateAsync(
                new VideoGenerationRequest(
                    anchoredPrompt, negativePrompt, scene.DurationSeconds,
                    ReferenceImages: selectedReferences, AspectRatio: context.AspectRatio),
                cancellationToken);
        }

        // Regenerating writes a new versioned key so the previous clip's bytes
        // stay recoverable even though its Asset row becomes Superseded.
        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{context.ContentProjectId}/scenes/{scene.Id}/visual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.mp4",
            result.VideoBytes,
            cancellationToken);

        // A scene switches type over its life, so retire BOTH old visual kinds.
        await _assetService.SupersedeSceneAssetsAsync(context.ContentProjectId, scene.Id, AssetType.Video, cancellationToken);
        await _assetService.SupersedeSceneAssetsAsync(context.ContentProjectId, scene.Id, AssetType.Image, cancellationToken);

        await _assetService.CreateAsync(
            context.ContentProjectId,
            new CreateAssetRequest(scene.Id, AssetType.Video, result.Model, anchoredPrompt, storedPath, scene.DurationSeconds, null, null),
            cancellationToken);

        if (anchorModel != "none")
        {
            await _usageTracker.RecordAsync(
                new RecordUsageInput("gemini", anchorModel, "scene_anchor_generation", _pricing.ImageUsd, context.ContentProjectId, scene.Id),
                cancellationToken);
        }
        var videoTier = Enum.TryParse<Domain.Generation.VideoModelTier>(scene.ModelTier, ignoreCase: true, out var t)
            ? t
            : Domain.Generation.VideoModelTier.Lite;
        await _usageTracker.RecordAsync(
            new RecordUsageInput("gemini", result.Model, "video_generation", _pricing.VideoUsdPerSecondFor(videoTier) * scene.DurationSeconds, context.ContentProjectId, scene.Id),
            cancellationToken);
        await _quotaManager.RecordVideoGenerationAsync(context.ContentProjectId, scene.Id, cancellationToken);
    }

    /// <summary>
    /// The single deterministic Scene-data -> prompt step, shared with the
    /// Google Flow plan (see <see cref="VideoPromptBuilder.Build"/>). The
    /// Character/Environment lines are emitted only when that anchor actually
    /// exists on the context; when the per-scene <paramref name="selection"/>
    /// resolved real names (a tagged Story scene), the character/location
    /// text names them instead of the generic sentence.
    /// </summary>
    private static string BuildClipPrompt(SceneGenerationContext context, SceneResponse scene, string action, CameraMovement camera, SceneReferenceSelection selection) =>
        VideoPromptBuilder.Build(new VideoPromptSpec(
            Action: action,
            Camera: camera,
            HasCharacterReference: context.CharacterReference is not null && scene.CharacterOnScreen != false,
            HasEnvironmentReference: context.EnvironmentReference is not null,
            StyleGuidance: context.Style.VisualStyleGuidance,
            DurationSeconds: scene.DurationSeconds,
            AspectRatio: context.AspectRatio,
            CharacterLabels: selection.CharacterLabels,
            LocationLabel: selection.LocationLabel,
            CharacterBehaviorProfiles: context.CharacterBehaviorProfiles,
            Shot: Enum.TryParse<ShotSize>(scene.ShotSize, ignoreCase: true, out var shot) ? shot : ShotSize.Unspecified));

    private async Task GenerateStillAsync(SceneGenerationContext context, SceneResponse scene, string prompt, string? negativePrompt, IReadOnlyList<ReferenceImage> selectedReferences, CancellationToken cancellationToken)
    {
        // Stills now get the approved references too (they were skipped before)
        // so an image scene matches the character/environment of the video scenes.
        var result = await _imageProvider.GenerateAsync(
            new ImageGenerationRequest(prompt, negativePrompt, selectedReferences),
            cancellationToken);

        var extension = result.MimeType.Contains("png") ? "png" : "jpg";
        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{context.ContentProjectId}/scenes/{scene.Id}/visual-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}",
            result.ImageBytes,
            cancellationToken);

        await _assetService.SupersedeSceneAssetsAsync(context.ContentProjectId, scene.Id, AssetType.Video, cancellationToken);
        await _assetService.SupersedeSceneAssetsAsync(context.ContentProjectId, scene.Id, AssetType.Image, cancellationToken);

        // Scene-scoped Image asset (approved AssetReferences live in their own
        // table now - the renderer/overview tell a scene still apart by SceneId).
        await _assetService.CreateAsync(
            context.ContentProjectId,
            new CreateAssetRequest(scene.Id, AssetType.Image, result.Model, prompt, storedPath, scene.DurationSeconds, null, null),
            cancellationToken);

        await _usageTracker.RecordAsync(
            new RecordUsageInput("gemini", result.Model, "image_generation", _pricing.ImageUsd, context.ContentProjectId, scene.Id),
            cancellationToken);
        await _quotaManager.RecordImageGenerationAsync(context.ContentProjectId, scene.Id, cancellationToken);

        _logger.LogInformation("Still image generated for scene {SceneId} ({Credits} credits)", scene.Id, _flow.CreditsPerImage);
    }

    public async Task GenerateVoiceAsync(SceneGenerationContext context, SceneResponse scene, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scene.Narration))
        {
            return;
        }

        // ITtsService synthesizes AND validates: it throws AudioValidationException
        // if the narration audio is missing, unreadable, zero-length, or silent,
        // which fails the generation rather than letting a caption-only clip
        // through. The spoken text is the scene narration; the on-screen caption
        // (scene.CaptionText / EffectiveCaptionText) is a separate concept and is
        // never sent to TTS.
        var result = await _ttsService.SynthesizeAsync(
            new TtsSynthesisRequest(scene.Narration, context.Voice),
            cancellationToken);

        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{context.ContentProjectId}/scenes/{scene.Id}/voice-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.wav",
            result.AudioBytes,
            cancellationToken);

        await _assetService.SupersedeSceneAssetsAsync(context.ContentProjectId, scene.Id, AssetType.Voice, cancellationToken);

        await _assetService.CreateAsync(
            context.ContentProjectId,
            new CreateAssetRequest(scene.Id, AssetType.Voice, result.Model, scene.Narration, storedPath, result.DurationSeconds, null, null),
            cancellationToken);

        // Capture the real narration timing now, so the renderer and caption
        // segmenter reuse it instead of recomputing on every render.
        var timing = _audioTiming.Compute(scene.Narration, result.DurationSeconds);
        await _storyboardService.SetSceneAudioTimingAsync(
            context.ContentProjectId, scene.Id, _audioTiming.Serialize(timing), cancellationToken);

        _logger.LogInformation(
            "Narration audio for scene {SceneId}: {Duration:0.00}s, {SampleRate}Hz x{Channels}, peak {Peak}",
            scene.Id, result.DurationSeconds, result.Validation.SampleRate, result.Validation.Channels, result.Validation.PeakAmplitude);

        var estimatedCost = result.IsFree ? 0m : (decimal)scene.Narration.Length / 1000m * _pricing.TtsUsdPer1000Chars;
        await _usageTracker.RecordAsync(
            new RecordUsageInput(result.IsFree ? "kokoro" : "gemini", result.Model, "tts_generation", estimatedCost, context.ContentProjectId, scene.Id),
            cancellationToken);
    }

    private async Task<IReadOnlyList<ReferenceImage>> LoadReferenceImagesAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var images = await _assetReferenceService.LoadApprovedImagesAsync(contentProjectId, cancellationToken);

        if (images.Count < 2)
        {
            _logger.LogWarning(
                "Character and environment anchors are required for ContentProject {ContentProjectId} before clips can be generated.",
                contentProjectId);
        }

        return images;
    }
}
