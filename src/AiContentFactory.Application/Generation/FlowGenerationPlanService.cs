using System.Text;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <param name="RecommendedModel">"Fast" / "Lite" for an AI_VIDEO scene; null otherwise.</param>
/// <param name="FlowVideoPrompt">
/// Ready to paste into Google Flow's prompt box - or any other manual
/// image/text-to-video tool's. Built by <see cref="VideoPromptBuilder.BuildNarrative"/>:
/// one flowing cinematic paragraph with named-character appositive clauses
/// and short inline "(refer to the attached {Name} reference)" tags, since
/// this is a manual copy-paste workflow where the reference IMAGES never
/// travel with the copied text. Empty (never raw narration/dialogue - see
/// <see cref="IsUnprompted"/>) when the scene has no vetted visual prompt
/// yet, so there is nothing unsafe to accidentally paste into Flow.
/// </param>
/// <param name="ReferenceImageUrls">API-relative URLs of the approved Character/Environment anchors to attach in Flow.</param>
/// <param name="CharacterReferenceImageUrl">
/// The project's approved Character anchor, unconditional on
/// <see cref="CharacterRequired"/> - a manual "copy this image" action should
/// offer it whenever one exists, not only on the scenes the allocator scored
/// as needing the protagonist on screen.
/// </param>
/// <param name="EnvironmentReferenceImageUrl">
/// The scene's matched Environment/location anchor, resolved from its tagged
/// RelevantReferenceLabels - a manual "copy this image" action should offer
/// it whenever one exists for this scene.
/// </param>
/// <param name="CharacterReferenceImages">
/// Every named Character reference this scene's tagged RelevantReferenceLabels
/// actually matched (see <see cref="ReferenceMatcher.Match"/>) - unlike the
/// legacy <see cref="CharacterReferenceImageUrl"/>, which only ever exposes
/// the project's single first-approved Character anchor, this list can carry
/// two or more entries when a scene names multiple characters at once (e.g.
/// "Milo" AND "Mimi"), so a manual "copy this image" action can offer every
/// character the scene actually needs, not just one. Empty when the scene has
/// no tagged/matched Character references - never null. Purely additive:
/// <see cref="CharacterReferenceImageUrl"/> is unchanged and still populated
/// exactly as before.
/// </param>
/// <param name="KeyframeImageUrl">
/// API-relative URL of this scene's own approved Keyframe (see
/// <c>Scene.KeyframeAssetId</c>/<c>SceneKeyframeService</c>) - null unless the
/// user generated and approved one in Step 5's two-stage workflow. When
/// present, a manual Flow user should download and attach THIS image (not the
/// generic Character/Environment anchors) as the image-to-video input, since
/// it already locks in the scene's specific composition/lighting.
/// </param>
/// <param name="KeyframeStatus">Raw <c>Scene.KeyframeStatus</c> ("None"/"Generating"/"Generated"/"Approved"/"Failed") - drives which Keyframe action button (Generate/Approve/Regenerate) the UI shows.</param>
/// <param name="KeyframeApproved">True once the scene's Keyframe has been reviewed and approved - convenience bool alongside <see cref="KeyframeStatus"/>, since "is it usable for a video generation yet" is checked often.</param>
/// <param name="MotionPrompt">
/// The scene's dedicated motion prompt (<c>Scene.MotionPrompt</c>) when the
/// user hand-edited one for the Keyframe workflow; falls back to
/// <see cref="FlowVideoPrompt"/> otherwise. Focuses on movement/camera/
/// temporal progression rather than re-describing appearance already locked
/// in by the Keyframe image.
/// </param>
/// <param name="RecommendFreeTool">
/// Best-effort recommendation for whether this clip is safe to generate in a
/// free external tool without the character anchor breaking story continuity.
/// True when there is no approved Character reference to keep consistent in
/// the first place, or when this specific clip's own AI-authored action text
/// (see <see cref="VideoPromptBuilder"/>'s action - the same text the prompt
/// itself is built from) has no on-screen-person cues. Defaults to false
/// (recommend the Character anchor) whenever unsure, since that is the safe
/// direction - a missed cue just means the user is nudged toward pasting the
/// reference image they didn't strictly need, not the other way round.
/// </param>
/// <param name="IsUnprompted">
/// True when this scene has no AI-suggested/hand-edited prompt and no written
/// visual description. When true, both <see cref="FlowVideoPrompt"/> (video/
/// motion scenes) and <see cref="ImagePrompt"/> (still-image scenes) are
/// deliberately left empty rather than falling back to raw
/// <c>Narration</c>/dialogue text (which is voiceover script, not a visual
/// description, and must never be dressed up as one) - the UI must render
/// its own warning using this flag, and the batch "copy all" export
/// (<see cref="FlowGenerationPlan.CopyAllText"/>) skips these scenes
/// entirely rather than embed a placeholder inline.
/// </param>
public record FlowPlanScene(
    Guid SceneId,
    int SceneNumber,
    string GenerationType,
    string? RecommendedModel,
    int EstimatedCredits,
    int Priority,
    string NarrationText,
    string CaptionText,
    string VisualDescription,
    string FlowVideoPrompt,
    string ImagePrompt,
    bool CharacterRequired,
    IReadOnlyList<string> ReferenceImageUrls,
    string? CharacterReferenceImageUrl,
    string? EnvironmentReferenceImageUrl,
    bool RecommendFreeTool,
    string? Rationale,
    bool SkipGeneration = false,
    int DurationSeconds = 0,
    bool IsUnprompted = false,
    IReadOnlyList<NamedReferenceImage>? CharacterReferenceImages = null,
    string? KeyframeImageUrl = null,
    bool KeyframeApproved = false,
    string? MotionPrompt = null,
    string KeyframeStatus = "None",
    string ShotSize = "Unspecified",
    bool? CharacterOnScreen = null,
    string FirstFramePrompt = "")
{
    private static readonly IReadOnlyList<NamedReferenceImage> Empty = Array.Empty<NamedReferenceImage>();

    /// <summary>
    /// Non-null view of <see cref="CharacterReferenceImages"/>. The primary
    /// constructor parameter stays nullable (matching every other optional
    /// <see cref="FlowPlanScene"/> field's default-null convention above) but
    /// <see cref="FlowGenerationPlanService"/> always passes a real (possibly
    /// empty) list, so this property is what every caller/test should read.
    /// </summary>
    public IReadOnlyList<NamedReferenceImage> CharacterReferenceImages { get; init; } = CharacterReferenceImages ?? Empty;
}

/// <summary>
/// One named Character reference's approved anchor image, scoped to a single
/// Flow-export scene - see <see cref="FlowPlanScene.CharacterReferenceImages"/>.
/// </summary>
/// <param name="Name">The character's real name/label (e.g. "Milo"), as tagged/approved.</param>
/// <param name="Url">API-relative URL of the approved anchor image, same shape as <see cref="FlowPlanScene.CharacterReferenceImageUrl"/>.</param>
public record NamedReferenceImage(string Name, string Url);

/// <summary>
/// Everything the user needs to run the video-generation step in Google Flow
/// efficiently. The app never calls Flow - it prepares prompts, reference
/// assets, a model recommendation per scene, and a credit budget so the user
/// spends their free daily Flow credits deliberately (and never has to spend
/// all of them).
/// </summary>
public record FlowGenerationPlan(
    int DailyBudgetCredits,
    int UsedCredits,
    int RemainingCredits,
    int PlannedCredits,
    int ScenesRequiringFlow,
    bool WithinBudget,
    string FastModelLabel,
    string LiteModelLabel,
    IReadOnlyList<FlowPlanScene> Scenes,
    string CopyAllText,
    string AspectRatio = "9:16");

public interface IFlowGenerationPlanService
{
    Task<FlowGenerationPlan> BuildAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class FlowGenerationPlanService : IFlowGenerationPlanService
{
    private readonly IContentProjectRepository _projectRepository;
    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IAssetReferenceRepository _assetReferenceRepository;
    private readonly ICreditLedger _creditLedger;
    private readonly IStoryVisualContextResolver _storyVisualContextResolver;
    private readonly CreditCostOptions _costs;
    private readonly FlowModelOptions _models;

    public FlowGenerationPlanService(
        IContentProjectRepository projectRepository,
        IStoryboardRepository storyboardRepository,
        IAssetReferenceRepository assetReferenceRepository,
        ICreditLedger creditLedger,
        IStoryVisualContextResolver storyVisualContextResolver,
        IOptions<CreditCostOptions> costs,
        IOptions<FlowModelOptions> models)
    {
        _projectRepository = projectRepository;
        _storyboardRepository = storyboardRepository;
        _assetReferenceRepository = assetReferenceRepository;
        _creditLedger = creditLedger;
        _storyVisualContextResolver = storyVisualContextResolver;
        _costs = costs.Value;
        _models = models.Value;
    }

    public async Task<FlowGenerationPlan> BuildAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var style = PresetCatalog.ResolveStyle(project.StylePresetId);
        var storyboard = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken);
        var references = await _assetReferenceRepository.GetByProjectAsync(contentProjectId, cancellationToken);
        var usage = await _creditLedger.GetDailyUsageAsync(cancellationToken);

        // Name -> appearance-description lookup from the Story "bible", so
        // the Flow export's [CHARACTER REF]/[LOCATION] lines can say what a
        // named character/location actually looks like, not just their name.
        // Null for every non-Story project (the normal/default case).
        var visualDescriptions = await _storyVisualContextResolver.GetCastAndLocationVisualDescriptionsAsync(contentProjectId, cancellationToken);

        // Name -> opted-in CharacterBehaviorProfile lookup from the Story
        // "bible", resolved once alongside visualDescriptions above. Null for
        // every non-Story project (the normal/default case).
        var behaviorProfiles = await _storyVisualContextResolver.GetCharacterBehaviorProfilesAsync(contentProjectId, cancellationToken);

        // The approved Character / Environment anchors, kept distinct so a scene
        // can say exactly which references it has (both are optional).
        string? UrlFor(AssetReferenceType type)
        {
            var r = references.FirstOrDefault(x => x.Type == type && x.Status == AssetReferenceStatus.Approved && !string.IsNullOrWhiteSpace(x.ImagePath));
            return r is null ? null : $"/content-projects/{contentProjectId}/asset-references/{r.Id}/file";
        }

        var characterUrl = UrlFor(AssetReferenceType.Character);
        var environmentUrl = UrlFor(AssetReferenceType.Environment);

        // Approved rows with real bytes, for the same per-scene named-tag
        // match ReferenceMatcher runs for the direct Veo path - only
        // metadata (Type/Label) is needed here, never image bytes, since the
        // Flow export only ever emits names/URLs, not attached images.
        var approvedNamedReferences = references
            .Where(r => r.Status == AssetReferenceStatus.Approved && !string.IsNullOrWhiteSpace(r.ImagePath))
            .ToList();

        var sceneResponses = storyboard is null
            ? new List<SceneResponse>()
            : storyboard.Scenes.OrderBy(s => s.SceneNumber).Select(SceneResponse.FromDomain).ToList();

        var scenes = sceneResponses
            .Select(s => BuildScene(s, style.VisualStyleGuidance, characterUrl, environmentUrl, approvedNamedReferences, visualDescriptions, behaviorProfiles, contentProjectId))
            .ToList();

        // A scene the user already has a clip for needs no Flow work and no credits.
        var plannedCredits = scenes.Where(s => s.GenerationType == "AI_VIDEO" && !s.SkipGeneration).Sum(s => s.EstimatedCredits);
        var scenesRequiringFlow = scenes.Count(s => s.GenerationType == "AI_VIDEO" && !s.SkipGeneration);

        return new FlowGenerationPlan(
            DailyBudgetCredits: _costs.DailyBudgetCredits,
            UsedCredits: usage.Used,
            RemainingCredits: usage.Remaining,
            PlannedCredits: plannedCredits,
            ScenesRequiringFlow: scenesRequiringFlow,
            WithinBudget: plannedCredits <= usage.Remaining,
            FastModelLabel: _models.FastModel,
            LiteModelLabel: _models.LiteModel,
            Scenes: scenes,
            CopyAllText: BuildCopyAll(scenes),
            AspectRatio: project.AspectRatio);
    }

    private FlowPlanScene BuildScene(
        SceneResponse scene, string styleGuidance, string? characterUrl, string? environmentUrl,
        IReadOnlyList<AssetReference> approvedNamedReferences, IReadOnlyDictionary<string, string>? visualDescriptions,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles,
        Guid contentProjectId)
    {
        var generationType = SceneResponse.GenerationTypeOf(ParseVisualType(scene.VisualType));
        var tier = Enum.TryParse<VideoModelTier>(scene.ModelTier, ignoreCase: true, out var t) ? t : (VideoModelTier?)null;

        // Character reference is relevant only when the scene features the
        // protagonist AND an approved Character anchor exists; Environment always
        // helps if one exists. Both are optional. CharacterRequired is the
        // prompt agent's shot-level decision when it made one (see
        // SceneResponse.FromDomain), the legacy heuristic otherwise.
        var hasCharacter = scene.CharacterRequired && characterUrl is not null;
        var hasEnvironment = environmentUrl is not null;

        var recommendedModel = generationType == "AI_VIDEO" ? (tier ?? VideoModelTier.Lite).ToString() : null;
        var estimatedCredits = generationType switch
        {
            "AI_VIDEO" => _costs.VideoCreditsFor(tier ?? VideoModelTier.Lite),
            "AI_IMAGE" => _costs.ImageCredits,
            _ => 0
        };

        // Same per-scene tagged-name match SceneAssetGenerator runs for the
        // direct Veo call (see ReferenceMatcher) - so the Flow export can
        // name the actual selected character(s)/location, not just say
        // "a Character reference exists".
        var matched = ReferenceMatcher.Match(
            approvedNamedReferences, scene.RelevantReferenceLabels, scene.Narration,
            typeOf: r => r.Type, labelOf: r => r.Label);

        // The shot was judged to show no recurring character: never attach or
        // name one, whatever older narration-based tags still say - otherwise
        // the character image drags the character into e.g. a pure landscape
        // or a shot of other animals.
        if (scene.CharacterOnScreen == false)
        {
            matched = matched.Where(r => r.Type != AssetReferenceType.Character).ToList();
        }
        var (characterLabels, locationLabel) = ReferenceMatcher.SplitNames(matched, r => r.Type, r => r.Label);

        // The scene's own matched Environment reference (if any), not the
        // legacy project-level single-slot environmentUrl - this is what
        // makes the "copy this image" action scene-specific, matching what
        // the [LOCATION] prompt line above already says.
        var matchedEnvironment = matched.FirstOrDefault(r => r.Type == AssetReferenceType.Environment);
        var environmentReferenceImageUrl = matchedEnvironment is null
            ? null
            : $"/content-projects/{contentProjectId}/asset-references/{matchedEnvironment.Id}/file";

        // Every named Character this scene's tags matched (not just one) - see
        // FlowPlanScene.CharacterReferenceImages. Every entry in `matched` is
        // guaranteed a non-null Label by ReferenceMatcher.Match, so no null-check
        // needed here beyond the Type filter.
        var characterReferenceImages = matched
            .Where(r => r.Type == AssetReferenceType.Character)
            .Select(r => new NamedReferenceImage(r.Label!, $"/content-projects/{contentProjectId}/asset-references/{r.Id}/file"))
            .ToList();

        var isUnprompted = IsUnprompted(scene);
        var promptAction = ResolvePromptAction(scene);
        var flowPrompt = isUnprompted
            ? string.Empty
            : ComposeFlowPrompt(scene, promptAction!, styleGuidance, hasCharacter, hasEnvironment, characterLabels, locationLabel, visualDescriptions, behaviorProfiles);
        var imagePrompt = isUnprompted
            ? string.Empty
            : ComposeImagePrompt(promptAction!, styleGuidance, hasCharacter, characterLabels, visualDescriptions, behaviorProfiles);

        // Two-step Flow workflow (video scenes): a still first frame made in
        // Flow's image tool, then a motion-only prompt that animates it.
        var shot = Enum.TryParse<ShotSize>(scene.ShotSize, ignoreCase: true, out var parsedShot) ? parsedShot : ShotSize.Unspecified;
        var firstFramePrompt = isUnprompted || generationType != "AI_VIDEO"
            ? string.Empty
            : ImagePromptComposer.Compose(promptAction!, styleGuidance, hasCharacter, characterLabels, visualDescriptions, behaviorProfiles, shot);
        var composedMotionPrompt = isUnprompted
            ? string.Empty
            : VideoPromptBuilder.BuildMotion(new VideoPromptSpec(
                Action: promptAction,
                Camera: CameraOf(scene),
                HasCharacterReference: hasCharacter,
                HasEnvironmentReference: hasEnvironment,
                StyleGuidance: null,
                DurationSeconds: scene.DurationSeconds,
                AspectRatio: "9:16",
                CharacterLabels: characterLabels,
                CharacterBehaviorProfiles: behaviorProfiles,
                Shot: shot));

        var referenceUrls = new[] { hasCharacter ? characterUrl : null, hasEnvironment ? environmentUrl : null }
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList();

        // Nothing to keep consistent if there's no approved Character anchor at
        // all; otherwise recommend the free route only when this clip's own
        // action doesn't put a person on screen. This heuristic may still fall
        // back to raw Narration text (see ResolveHeuristicAction) - that is
        // safe here because the result only ever feeds a boolean
        // recommendation, never the pasteable FlowVideoPrompt itself.
        var recommendFreeTool = generationType == "AI_VIDEO"
            && (characterUrl is null || !MentionsOnScreenCharacter(ResolveHeuristicAction(scene)));

        // Exposed as soon as one exists (Generated OR Approved) - unlike the
        // Character/Environment anchors above (which stay gated to Approved,
        // since those are reused across scenes/episodes), the user must be
        // able to PREVIEW this scene's own Keyframe before deciding whether to
        // approve it. keyframeApproved is the separate "is it actually usable
        // for a video generation yet" signal.
        var keyframeApproved = string.Equals(scene.KeyframeStatus, "Approved", StringComparison.OrdinalIgnoreCase);
        var keyframeImageUrl = scene.KeyframeAssetId is { } keyframeAssetId
            ? $"/content-projects/{contentProjectId}/assets/{keyframeAssetId}/file"
            : null;
        var motionPrompt = !string.IsNullOrWhiteSpace(scene.MotionPrompt) ? scene.MotionPrompt : composedMotionPrompt;

        return new FlowPlanScene(
            scene.Id,
            scene.SceneNumber,
            generationType,
            recommendedModel,
            estimatedCredits,
            scene.AiVideoPriority,
            scene.Narration,
            scene.EffectiveCaptionText,
            scene.VisualDescription,
            flowPrompt,
            imagePrompt,
            scene.CharacterRequired,
            referenceUrls,
            characterUrl,
            environmentReferenceImageUrl,
            recommendFreeTool,
            scene.AllocationRationale,
            scene.SkipGeneration,
            scene.DurationSeconds,
            isUnprompted,
            characterReferenceImages,
            keyframeImageUrl,
            keyframeApproved,
            motionPrompt,
            scene.KeyframeStatus,
            scene.ShotSize,
            scene.CharacterOnScreen,
            firstFramePrompt);
    }

    /// <summary>
    /// The Flow video prompt's action text - ONLY the agent-written shot
    /// description (<c>GenerationPrompt</c>) or, failing that, a hand-written
    /// <c>VisualDescription</c>. Deliberately does NOT fall back to raw
    /// <c>Narration</c> (the TTS/voiceover script text, not a visual
    /// description - see <see cref="IsUnprompted"/>'s remarks for the bug
    /// this fixes): a scene with neither returns <c>null</c>, and
    /// <see cref="BuildScene"/> leaves <see cref="FlowPlanScene.FlowVideoPrompt"/>
    /// empty for it rather than composing a prompt from dialogue text.
    /// </summary>
    private static string? ResolvePromptAction(SceneResponse scene) =>
        !string.IsNullOrWhiteSpace(scene.GenerationPrompt)
            ? scene.GenerationPrompt!
            : !string.IsNullOrWhiteSpace(scene.VisualDescription)
                ? scene.VisualDescription
                : null;

    /// <summary>
    /// <see cref="FlowPlanScene.RecommendFreeTool"/>-only action text: same
    /// preference order as <see cref="ResolvePromptAction"/>, but falls back
    /// to raw <c>Narration</c> and finally <see cref="VideoPromptBuilder.DefaultAction"/>
    /// rather than returning <c>null</c>, since this text only ever feeds a
    /// best-effort on-screen-person keyword scan for a boolean
    /// recommendation - it is never exposed as/pasted into a Flow prompt, so
    /// the raw-narration restriction that applies to
    /// <see cref="ResolvePromptAction"/> does not apply here.
    /// </summary>
    private static string ResolveHeuristicAction(SceneResponse scene) =>
        !string.IsNullOrWhiteSpace(scene.GenerationPrompt)
            ? scene.GenerationPrompt!
            : !string.IsNullOrWhiteSpace(scene.VisualDescription)
                ? scene.VisualDescription
                : !string.IsNullOrWhiteSpace(scene.Narration)
                    ? scene.Narration
                    : VideoPromptBuilder.DefaultAction;

    /// <summary>
    /// True exactly when <see cref="ResolvePromptAction"/> has nothing real to
    /// work with - neither an AI-suggested/hand-edited <c>GenerationPrompt</c>
    /// (set only by <c>StoryboardService.SuggestScenePromptAsync</c>'s "Gợi ý
    /// prompt" action or a hand-edited prompt) nor a written
    /// <c>VisualDescription</c> (blank on every freshly-split scene -
    /// <see cref="Storyboards.ClipPlanService.GenerateAsync"/> creates scenes
    /// with <c>VisualDescription = string.Empty</c> - and only ever filled in
    /// by a user hand-editing the scene). When true, <see cref="BuildScene"/>
    /// leaves <see cref="FlowPlanScene.FlowVideoPrompt"/> empty instead of
    /// composing a prompt from raw <c>Narration</c> (the TTS/voiceover script
    /// text, never a visual description) or a generic default dressed up as a
    /// deliberate, vetted choice - a real, previously-hit bug (raw dialogue
    /// text leaking into the copy-pasted Flow prompt). The scene's
    /// <c>CameraMovement</c> is still <see cref="CameraMovement.Unspecified"/>
    /// in this state (nothing but <c>PromptAgent</c> - run by the same "Gợi ý
    /// prompt" action - ever sets it).
    /// </summary>
    private static bool IsUnprompted(SceneResponse scene) =>
        string.IsNullOrWhiteSpace(scene.GenerationPrompt) && string.IsNullOrWhiteSpace(scene.VisualDescription);

    /// <summary>
    /// Best-effort keyword scan over this clip's own action text for
    /// on-screen-person cues (pronouns, described expressions/gestures,
    /// dialogue-shaped narration) as opposed to a pure establishing/graphic/
    /// product shot. Not a guarantee - see <see cref="FlowPlanScene.RecommendFreeTool"/>
    /// for why a miss here is safe by design.
    /// </summary>
    private static readonly string[] OnScreenCharacterCues =
    {
        " he ", " she ", " his ", " her ", " him ", " himself ", " herself ",
        " i ", " i'm ", " my ", " we ", " our ",
        "the man", "the woman", "the character", "the protagonist", "the presenter", "the narrator",
        "face", "expression", "smil", "laugh", "cries", "crying", " eyes", "hand",
        "walk", "sit", "stand", "turns to", "turning to", "look at", "looking at", "look up", "look down", "reach",
        "nod", "speak", "talk", "gestur", "lean", "hold", "picks up", "picking up", "raises", "raising",
        "wearing", "dressed", "portrait", "close-up of a", "close-up on",
    };

    private static bool MentionsOnScreenCharacter(string action)
    {
        var text = " " + action.ToLowerInvariant() + " ";
        return OnScreenCharacterCues.Any(text.Contains);
    }

    /// <summary>
    /// Builds this scene's Flow export block via <see cref="VideoPromptBuilder.BuildNarrative"/>
    /// - a single flowing cinematic paragraph (not the earlier bracketed
    /// shape - see git history and <see cref="VideoPromptBuilder.BuildNarrative"/>'s
    /// remarks for why), since a human pastes this manually and the model
    /// itself reads natural prose better than labelled lines.
    ///
    /// Only called when <see cref="IsUnprompted"/> is false - <c>action</c> is
    /// always a real, non-null <see cref="ResolvePromptAction"/> result, never
    /// raw <c>Narration</c>. <see cref="FlowPlanScene.FlowVideoPrompt"/> is
    /// pasted directly into Google Flow, an English-only text-to-video model -
    /// it must NEVER contain Vietnamese (or any) UI/meta commentary, only
    /// clean generation content. The "unprompted" signal (see
    /// <see cref="IsUnprompted"/>) is carried ONLY as a separate boolean field
    /// on <see cref="FlowPlanScene"/> for the frontend to render as its own
    /// Vietnamese warning banner OUTSIDE the copyable text.
    /// </summary>
    private static string ComposeFlowPrompt(
        SceneResponse scene, string action, string styleGuidance, bool hasCharacterReference, bool hasEnvironmentReference,
        IReadOnlyList<string> characterLabels, string? locationLabel, IReadOnlyDictionary<string, string>? visualDescriptions,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles)
    {
        var camera = CameraOf(scene);

        var locationVisualDescription = locationLabel is not null && visualDescriptions is not null
            && visualDescriptions.TryGetValue(locationLabel, out var locationDescription)
                ? locationDescription
                : null;

        return VideoPromptBuilder.BuildNarrative(new VideoPromptSpec(
            Action: action,
            Camera: camera,
            HasCharacterReference: hasCharacterReference,
            HasEnvironmentReference: hasEnvironmentReference,
            StyleGuidance: styleGuidance,
            DurationSeconds: scene.DurationSeconds,
            AspectRatio: "9:16",
            CharacterLabels: characterLabels,
            LocationLabel: locationLabel,
            CharacterVisualDescriptions: visualDescriptions,
            LocationVisualDescription: locationVisualDescription,
            CharacterBehaviorProfiles: behaviorProfiles,
            Shot: Enum.TryParse<ShotSize>(scene.ShotSize, ignoreCase: true, out var shot) ? shot : ShotSize.Unspecified));
    }

    /// <summary>The structured camera, falling back to the legacy free-text camera note.</summary>
    private static CameraMovement CameraOf(SceneResponse scene)
    {
        var camera = VideoPromptBuilder.ParseCamera(scene.CameraMovement);
        return camera != CameraMovement.Unspecified ? camera : VideoPromptBuilder.ParseCamera(scene.CameraDirection);
    }

    /// <summary>
    /// Builds this scene's Flow export "still image" prompt (<c>AI_IMAGE</c>
    /// scenes only). Only called when <see cref="IsUnprompted"/> is false -
    /// <paramref name="action"/> is always a real, non-null
    /// <see cref="ResolvePromptAction"/> result, the exact same value
    /// <see cref="ComposeFlowPrompt"/> uses for the video path - never raw
    /// <c>Narration</c> (the TTS/voiceover script text, not a visual
    /// description). <see cref="BuildScene"/> leaves
    /// <see cref="FlowPlanScene.ImagePrompt"/> empty instead whenever there is
    /// nothing vetted to compose from, mirroring <see cref="FlowPlanScene.FlowVideoPrompt"/>.
    ///
    /// Opens with "A photorealistic vertical 9:16 photograph:" rather than the
    /// earlier "Photorealistic cinematic ... still:" wording - "cinematic"
    /// implies motion, which reads oddly for a genuinely single generated
    /// image (this method is confirmed scoped to <c>AiImage</c>-typed scenes
    /// only - see <see cref="BuildScene"/>/<see cref="ImageGenerationService"/>
    /// and the frontend's <c>kind === "image" ? scene.imagePrompt : scene.flowVideoPrompt</c>
    /// branch), so the "photograph"/vertical-still framing itself is kept,
    /// just phrased without the contradictory "cinematic ... still" pairing.
    ///
    /// A thin wrapper over the shared <see cref="ImagePromptComposer"/> (also
    /// used by <c>SceneKeyframeService</c> for the real in-app Keyframe
    /// generation call, so the composition rules - never the old hardcoded
    /// "real human, correct anatomy" assumption, which is actively wrong for
    /// this project's own (feline) characters and an unjustified guess for
    /// any project - exist in exactly one place). Output is unchanged from
    /// before this extraction.
    /// </summary>
    private static string ComposeImagePrompt(
        string action, string styleGuidance, bool hasCharacterReference,
        IReadOnlyList<string> characterLabels, IReadOnlyDictionary<string, string>? visualDescriptions,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles) =>
        ImagePromptComposer.Compose(action, styleGuidance, hasCharacterReference, characterLabels, visualDescriptions, behaviorProfiles);

    /// <summary>
    /// The one-click export for the Google Flow step. English only - it is
    /// generation instructions for a model, not UI copy. One block per scene that
    /// still needs a Flow clip, in a fixed "--- SCENE n (Veo Fast - 20 credits) ---"
    /// header + prompt shape; the user pastes one block per Generate. Scenes the
    /// user already has a clip for are left out entirely.
    ///
    /// Unprompted scenes (see <see cref="IsUnprompted"/>) are also left out
    /// entirely rather than have their empty/placeholder <see cref="FlowPlanScene.FlowVideoPrompt"/>
    /// embedded mid-batch - a user pasting several scenes' blocks in one pass
    /// should never hit a broken/non-visual entry buried in the middle. A
    /// trailing English summary line reports how many were skipped, so the
    /// omission is visible rather than silent.
    /// </summary>
    private static string BuildCopyAll(IReadOnlyList<FlowPlanScene> scenes)
    {
        var eligible = scenes.Where(s => s.GenerationType == "AI_VIDEO" && !s.SkipGeneration).ToList();
        var video = eligible.Where(s => !s.IsUnprompted).ToList();
        var skippedCount = eligible.Count - video.Count;

        if (video.Count == 0)
        {
            return skippedCount > 0 ? SkippedScenesNote(skippedCount) : string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < video.Count; i++)
        {
            var s = video[i];
            var model = string.Equals(s.RecommendedModel, "Fast", StringComparison.OrdinalIgnoreCase) ? "Veo Fast" : "Veo Lite";
            sb.AppendLine($"--- SCENE {s.SceneNumber} ({model} - {s.EstimatedCredits} credits) ---");
            sb.AppendLine(s.FlowVideoPrompt);
            if (i < video.Count - 1)
            {
                sb.AppendLine();
            }
        }

        if (skippedCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(SkippedScenesNote(skippedCount));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>English-only trailing summary for scenes <see cref="BuildCopyAll"/> excluded because they have no vetted visual prompt yet.</summary>
    private static string SkippedScenesNote(int count) =>
        $"({count} scene(s) skipped - not yet prompted. Generate a prompt for each scene first, then re-copy.)";

    private static Domain.Storyboards.SceneVisualType ParseVisualType(string value) =>
        Enum.TryParse<Domain.Storyboards.SceneVisualType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : Domain.Storyboards.SceneVisualType.AiVideo;
}
