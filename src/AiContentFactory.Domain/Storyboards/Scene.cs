using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Storyboards;

public class Scene : BaseEntity
{
    public Guid StoryboardId { get; private set; }
    public int SceneNumber { get; private set; }
    public int DurationSeconds { get; private set; }

    /// <summary>
    /// What the narrator actually says - the text sent to TTS. Kept separate
    /// from <see cref="CaptionText"/>: the two diverge as soon as you want the
    /// spoken line phrased for the ear ("twenty twenty-six", contractions,
    /// filler) but the on-screen caption phrased for the eye ("2026", trimmed).
    /// </summary>
    public string Narration { get; private set; } = string.Empty;

    /// <summary>
    /// The on-screen caption text for this scene. Null means "use
    /// <see cref="Narration"/> verbatim" - the common case - so nothing breaks
    /// for scenes created before captions and narration were split.
    /// </summary>
    public string? CaptionText { get; private set; }

    /// <summary>The caption text to render: the explicit override if set, otherwise the narration.</summary>
    public string EffectiveCaptionText => string.IsNullOrWhiteSpace(CaptionText) ? Narration : CaptionText;

    public string VisualDescription { get; private set; } = string.Empty;

    /// <summary>
    /// Free-form camera note kept for display and for scenes created before
    /// <see cref="CameraMovement"/> existed. The prompt builder prefers the
    /// structured <see cref="CameraMovement"/>; it only parses this string when
    /// the structured value is <see cref="Storyboards.CameraMovement.Unspecified"/>.
    /// </summary>
    public string CameraDirection { get; private set; } = string.Empty;

    /// <summary>
    /// The deterministic camera behaviour for this scene, chosen by the prompt
    /// agent (or the user) from a fixed set. Drives the single "Camera:" line of
    /// the generated video prompt.
    /// </summary>
    public CameraMovement CameraMovement { get; private set; } = CameraMovement.Unspecified;

    /// <summary>The framing chosen by the prompt agent (or the user). Unspecified = no framing stated in the prompt.</summary>
    public ShotSize ShotSize { get; private set; } = ShotSize.Unspecified;

    /// <summary>
    /// Whether a recurring character (a named Story character or the project's
    /// main character) is actually visible in this shot, as decided from the
    /// shot's own visual content by the prompt agent. Null = never decided
    /// (scene prompted before this existed) - callers then fall back to their
    /// legacy heuristic, so existing scenes behave exactly as before.
    /// </summary>
    public bool? CharacterOnScreen { get; private set; }

    /// <summary>
    /// Serialized result of the latest AI clip check (a billable vision call),
    /// including the Asset id of the clip it judged - a result is only shown
    /// while that clip is still the scene's current video. Null = never checked.
    /// </summary>
    public string? ClipCheckJson { get; private set; }

    public string? VisualStyle { get; private set; }
    public string? GenerationPrompt { get; private set; }
    public string? NegativePrompt { get; private set; }
    public SceneVisualType VisualType { get; private set; }
    public string? Provider { get; private set; }
    public SceneStatus Status { get; private set; } = SceneStatus.Pending;

    /// <summary>
    /// 0-100. How much this scene benefits from real motion, set by the
    /// storyboard allocator. Drives whether it gets an AI video clip and at
    /// which tier - AI video is never pinned to a fixed timestamp.
    /// </summary>
    public int AiVideoPriority { get; private set; }

    /// <summary>Model tier chosen for a video scene ("Fast"/"Lite"); null for a still.</summary>
    public string? ModelTier { get; private set; }

    /// <summary>
    /// True when the user already has a video for this scene (imported their own
    /// .mp4) or explicitly asked to skip generation. The pipeline must not send
    /// a generation request, create a job, or estimate a generation cost for it.
    /// Cleared when the imported clip is removed so the scene can be built again.
    /// </summary>
    public bool SkipGeneration { get; private set; }

    /// <summary>Plain-language reason this scene is AI video vs. a still - shown in the storyboard review.</summary>
    public string? AllocationRationale { get; private set; }

    /// <summary>
    /// Serialized <c>AudioTiming</c> for this scene's narration, captured when
    /// the TTS ran (measured duration + word/sentence timings). Persisted so the
    /// renderer and caption segmenter use the real timing instead of
    /// recomputing it every render. Null until narration is generated.
    /// </summary>
    public string? AudioTimingJson { get; private set; }

    /// <summary>
    /// Newline-joined reference <see cref="AssetReference.Label"/> values
    /// (character/location names) that this scene's narration/visual
    /// description was found to reference, for Story-linked projects with
    /// multiple named references. Use <see cref="RelevantReferenceLabels"/>
    /// for the split list. Empty/null = "nothing recognized, or not a
    /// Story-linked project" - unaffected for every existing scene.
    /// </summary>
    public string? RelevantReferenceLabelsText { get; private set; }

    /// <summary>The split, deduplicated list backing <see cref="RelevantReferenceLabelsText"/>.</summary>
    public IReadOnlyList<string> RelevantReferenceLabels =>
        string.IsNullOrEmpty(RelevantReferenceLabelsText)
            ? Array.Empty<string>()
            : RelevantReferenceLabelsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Lifecycle of this scene's optional Keyframe - a still image generated
    /// and approved BEFORE video generation, so identity/composition/lighting
    /// are locked in and reviewable before spending on an 8s clip. Entirely
    /// optional: a scene that never generates one simply stays
    /// <see cref="Storyboards.KeyframeStatus.None"/> and nothing about its
    /// existing direct-to-video/image generation changes.
    /// </summary>
    public KeyframeStatus KeyframeStatus { get; private set; } = KeyframeStatus.None;

    /// <summary>
    /// FK-less pointer (same tolerant-cross-module-reference pattern used
    /// elsewhere in this codebase) to the <c>Asset</c> row (Type=Image,
    /// SceneId=this scene) holding the current Keyframe's bytes. Set by
    /// <see cref="MarkKeyframeGenerated"/>; a later regeneration overwrites it
    /// with the new Asset's id once the old one is superseded.
    /// </summary>
    public Guid? KeyframeAssetId { get; private set; }

    /// <summary>The prompt used to generate the current Keyframe - character appearance/composition/lighting/style only, never dialogue.</summary>
    public string? KeyframeImagePrompt { get; private set; }

    /// <summary>
    /// The prompt used to animate the approved Keyframe into video - camera
    /// movement, character/object motion, temporal progression. Deliberately
    /// separate from <see cref="GenerationPrompt"/> (the direct-to-video
    /// path's own action text) and from <see cref="KeyframeImagePrompt"/> -
    /// null means "compose one from the scene's existing action text at
    /// generation time" (see <c>SceneKeyframeService</c>).
    /// </summary>
    public string? MotionPrompt { get; private set; }

    private Scene()
    {
        // EF Core
    }

    internal static Scene Create(Guid storyboardId, int sceneNumber, int durationSeconds, string narration, string visualDescription, string cameraDirection, SceneVisualType visualType) => new()
    {
        StoryboardId = storyboardId,
        SceneNumber = sceneNumber,
        DurationSeconds = durationSeconds,
        Narration = narration ?? string.Empty,
        VisualDescription = visualDescription ?? string.Empty,
        CameraDirection = cameraDirection ?? string.Empty,
        VisualType = visualType
    };

    public void UpdateContent(int durationSeconds, string narration, string visualDescription, string cameraDirection, SceneVisualType visualType)
    {
        DurationSeconds = durationSeconds;
        Narration = narration ?? string.Empty;
        VisualDescription = visualDescription ?? string.Empty;
        CameraDirection = cameraDirection ?? string.Empty;
        VisualType = visualType;
        Touch();
    }

    /// <summary>
    /// Sets an explicit on-screen caption distinct from the spoken narration.
    /// Blank clears the override, so the caption falls back to the narration.
    /// </summary>
    public void SetCaptionText(string? captionText)
    {
        CaptionText = string.IsNullOrWhiteSpace(captionText) ? null : captionText.Trim();
        Touch();
    }

    /// <summary>
    /// Records the storyboard allocator's decision for this scene: how much it
    /// wants motion, the model tier (for video), and why.
    /// </summary>
    public void SetAllocation(int aiVideoPriority, string? modelTier, string? rationale)
    {
        AiVideoPriority = Math.Clamp(aiVideoPriority, 0, 100);
        ModelTier = string.IsNullOrWhiteSpace(modelTier) ? null : modelTier.Trim();
        AllocationRationale = string.IsNullOrWhiteSpace(rationale) ? null : rationale.Trim();
        Touch();
    }

    /// <summary>
    /// Overrides the video model tier ("Fast"/"Lite") for this scene without
    /// touching the allocator's priority or rationale - the user picking a model
    /// per clip in the build step. Blank clears it (back to the allocator's pick).
    /// </summary>
    public void SetModelTier(string? modelTier)
    {
        ModelTier = string.IsNullOrWhiteSpace(modelTier) ? null : modelTier.Trim();
        Touch();
    }

    /// <summary>
    /// Marks this scene as "already has a video / skip Veo" (true) or clears it
    /// (false). Set when the user imports their own clip; cleared when that clip
    /// is removed.
    /// </summary>
    public void SetSkipGeneration(bool skip)
    {
        SkipGeneration = skip;
        Touch();
    }

    /// <summary>Sets the deterministic camera behaviour for this scene.</summary>
    public void SetCameraMovement(CameraMovement cameraMovement)
    {
        CameraMovement = cameraMovement;
        Touch();
    }

    /// <summary>Sets the framing for this scene.</summary>
    public void SetShotSize(ShotSize shotSize)
    {
        ShotSize = shotSize;
        Touch();
    }

    /// <summary>Records whether a recurring character is visible in this shot. Null returns the scene to "not decided".</summary>
    public void SetCharacterOnScreen(bool? characterOnScreen)
    {
        CharacterOnScreen = characterOnScreen;
        Touch();
    }

    /// <summary>Stores the latest AI clip check result. Blank clears it.</summary>
    public void SetClipCheck(string? clipCheckJson)
    {
        ClipCheckJson = string.IsNullOrWhiteSpace(clipCheckJson) ? null : clipCheckJson;
        Touch();
    }

    /// <summary>Returns a scene to the un-generated state - used when its imported clip is removed.</summary>
    public void MarkPending()
    {
        Status = SceneStatus.Pending;
        Touch();
    }

    /// <summary>Stores the serialized narration timing captured at TTS time. Blank clears it.</summary>
    public void SetAudioTiming(string? audioTimingJson)
    {
        AudioTimingJson = string.IsNullOrWhiteSpace(audioTimingJson) ? null : audioTimingJson;
        Touch();
    }

    /// <summary>
    /// Records which reference <see cref="AssetReference.Label"/> values
    /// (character/location names) this scene's narration/visual description
    /// was found to reference. Trims, drops blanks, de-duplicates
    /// case-insensitively (keeping first-seen casing/order), then stores the
    /// result newline-joined. Empty input clears it.
    /// </summary>
    public void SetRelevantReferenceLabels(IEnumerable<string> labels)
    {
        var normalized = (labels ?? Enumerable.Empty<string>())
            .Select(l => l?.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        RelevantReferenceLabelsText = normalized.Length == 0 ? null : string.Join('\n', normalized);
        Touch();
    }

    public void SetGenerationPrompt(string prompt, string? negativePrompt, string? visualStyle, string provider)
    {
        GenerationPrompt = prompt;
        NegativePrompt = negativePrompt;
        VisualStyle = visualStyle;
        Provider = provider;
        Status = SceneStatus.PromptReady;
        Touch();
    }

    /// <summary>
    /// User picks AI video (expensive, moving) vs AI still image (cheap, a
    /// Ken-Burns pan at render time) for this scene. Changing it clears the
    /// stored prompt - a prompt written for one reads wrong for the other.
    /// </summary>
    public void SetVisualType(SceneVisualType visualType)
    {
        if (VisualType == visualType)
        {
            return;
        }

        VisualType = visualType;
        GenerationPrompt = null;
        NegativePrompt = null;
        if (Status == SceneStatus.PromptReady)
        {
            Status = SceneStatus.Pending;
        }
        Touch();
    }

    /// <summary>Hand-edited generation prompt from the wizard. Blank means "let the prompt agent write one at generation time".</summary>
    public void SetGenerationPromptText(string? prompt, string? negativePrompt)
    {
        GenerationPrompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();
        NegativePrompt = string.IsNullOrWhiteSpace(negativePrompt) ? null : negativePrompt.Trim();

        Status = GenerationPrompt is not null && Status == SceneStatus.Pending
            ? SceneStatus.PromptReady
            : Status;
        Touch();
    }

    public void MarkGenerating()
    {
        Status = SceneStatus.Generating;
        Touch();
    }

    public void MarkGenerated()
    {
        Status = SceneStatus.Generated;
        Touch();
    }

    public void MarkFailed()
    {
        Status = SceneStatus.Failed;
        Touch();
    }

    /// <summary>Throws if a Keyframe generation is already running - the duplicate-request guard for the keyframe endpoint.</summary>
    public void MarkKeyframeGenerating()
    {
        if (KeyframeStatus == KeyframeStatus.Generating)
        {
            throw new DomainException("This scene's Keyframe is already generating.");
        }

        KeyframeStatus = KeyframeStatus.Generating;
        Touch();
    }

    /// <summary>
    /// Records a freshly generated Keyframe. Demotes any prior Approved status
    /// back to Generated (unapproved) - a regenerated image must be reviewed
    /// again before it can anchor a video generation.
    /// </summary>
    public void MarkKeyframeGenerated(Guid assetId, string? prompt)
    {
        KeyframeAssetId = assetId;
        KeyframeImagePrompt = prompt;
        KeyframeStatus = KeyframeStatus.Generated;
        Touch();
    }

    public void MarkKeyframeFailed()
    {
        KeyframeStatus = KeyframeStatus.Failed;
        Touch();
    }

    /// <summary>Approves the current Keyframe so it can be used as a video generation's input image. Throws unless a Keyframe was just generated.</summary>
    public void ApproveKeyframe()
    {
        if (KeyframeStatus != KeyframeStatus.Generated)
        {
            throw new DomainException("The Keyframe must be generated before it can be approved.");
        }

        KeyframeStatus = KeyframeStatus.Approved;
        Touch();
    }

    /// <summary>Hand-edited or agent-composed motion prompt for animating the approved Keyframe. Blank clears it (falls back to a composed default at generation time).</summary>
    public void SetMotionPrompt(string? motionPrompt)
    {
        MotionPrompt = string.IsNullOrWhiteSpace(motionPrompt) ? null : motionPrompt.Trim();
        Touch();
    }
}
