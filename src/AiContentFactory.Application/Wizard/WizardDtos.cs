using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Scripts;

namespace AiContentFactory.Application.Wizard;

public enum WizardStep
{
    Template = 0,
    Idea = 1,
    Script = 2,
    References = 3,
    Generate = 4,
    Preview = 5,
    Export = 6
}

/// <summary>
/// What a clip looks like to someone who doesn't know what a Scene, an Asset,
/// or a SceneStatus is.
/// </summary>
public enum ClipState
{
    NotStarted = 0,
    Working = 1,
    Ready = 2,
    Failed = 3
}

/// <param name="PreviewUrl">
/// API-relative path to the clip's visual (video or image). The underlying
/// storage key is never exposed.
/// </param>
/// <param name="VisualType">"AiVideo" or "AiImage" - the frontend renders a &lt;video&gt; or an &lt;img&gt;.</param>
/// <param name="GenerationPrompt">
/// The prompt that will be / was sent to the model. Shown editable in the clip
/// list at the user's request - this is the one internal-ish field the wizard
/// deliberately surfaces.
/// </param>
/// <param name="AiVideoPriority">0-100, how much the storyboard allocator thought this scene needs motion.</param>
/// <param name="ModelTier">"Fast" / "Lite" for a video scene, null for a still.</param>
/// <param name="AllocationRationale">Plain-language "why AI video vs image" for the storyboard review.</param>
/// <param name="SkipGeneration">User flagged "already has a video" - no Veo request, job or cost for this clip.</param>
/// <param name="HasExistingVideo">A ready video asset exists for this scene (an imported .mp4).</param>
public record WizardClipDto(
    Guid Id,
    int Number,
    string Narration,
    int DurationSeconds,
    string State,
    string VisualType,
    string? GenerationPrompt,
    string? PreviewUrl,
    bool HasVoice,
    int AiVideoPriority,
    string? ModelTier,
    string CameraMovement,
    string? AllocationRationale,
    bool SkipGeneration,
    bool HasExistingVideo,
    /// <summary>Story character/location names this scene's narration mentioned (empty for a non-Story project, or a Story scene where nothing was recognized) - which named reference image(s) generation will actually use for this clip.</summary>
    IReadOnlyList<string> RelevantReferenceLabels,
    /// <summary>The latest AI clip check - only while it still describes this scene's CURRENT clip (null otherwise).</summary>
    Generation.ClipCheckResult? ClipCheck = null);

/// <param name="EstimatedForProject">Credits the current storyboard would draw if generated now.</param>
/// <param name="WithinBudget">False when <see cref="EstimatedForProject"/> exceeds what's left today - the UI warns and blocks.</param>
public record WizardCreditSummaryDto(
    int DailyBudget,
    int Reserved,
    int Used,
    int Remaining,
    int FailedToday,
    int EstimatedForProject,
    bool WithinBudget);

/// <param name="State">Pending / Generating / Completed / Failed / Retrying / Validated.</param>
public record WizardAttemptDto(
    Guid Id,
    string Kind,
    int? SceneNumber,
    string Provider,
    string Model,
    string? ModelTier,
    string State,
    int EstimatedCredits,
    int? ActualCredits,
    int AttemptNumber,
    string? FailureReason,
    double? AudioDurationSeconds,
    DateTimeOffset CreatedAt);

/// <summary>Step 6 status board: is there narration, are captions ready, did composition/render/validation pass.</summary>
/// <param name="AudioMode">"Original" (keep clip audio), "Generated" (AI voice-over), or "Muted".</param>
public record WizardCompositionStatusDto(
    string NarrationStatus,
    double NarrationSeconds,
    int ScenesWithNarration,
    int ScenesExpectingNarration,
    string CaptionStatus,
    string CompositionStatus,
    string RenderStatus,
    string ValidationStatus,
    string AudioMode,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> ValidationWarnings);

public record WizardValidationDto(
    bool HasRun,
    bool Ok,
    string Summary,
    double DurationSeconds,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? CheckedAt);

/// <param name="Busy">
/// True while a background job is running. Derived from GenerationProgress,
/// not from the project status - script generation runs while the project is
/// still Draft, so status alone can't answer this.
/// </param>
public record WizardProgressDto(
    bool Busy,
    string Stage,
    string Label,
    int Percent,
    int CompletedUnits,
    int TotalUnits);

/// <param name="Advice">The QA agent's notes - the only part of the score that tells the user what to change.</param>
public record WizardQaDto(double Overall, string Label, string Advice);

public record WizardPresetSelectionDto(
    string? TemplateId,
    string? TemplateName,
    string? StylePresetId,
    string? StyleName,
    string? VoicePresetId,
    string? VoiceName,
    string? CaptionPresetId,
    string? CaptionName);

/// <summary>
/// The single payload behind the wizard. Deliberately omits provider names,
/// storage paths, generation prompts, job ids, and raw status/enum names -
/// anything that would leak how the pipeline works. The Advanced page keeps
/// using the original endpoints for all of that.
/// </summary>
/// <param name="Blockers">
/// Plain-language reasons the user can't move forward yet ("Cần tạo kịch bản
/// trước"), never a status name.
/// </param>
/// <param name="Failed">
/// The last background job failed. The wizard surfaces this as "lần chạy
/// trước bị lỗi, thử lại" plus a retry, rather than a Failed status badge.
/// </param>
public record ProjectOverviewResponse(
    Guid Id,
    string Title,
    string? Topic,
    string? Niche,
    int TargetDurationSeconds,
    string AspectRatio,
    string Language,
    WizardPresetSelectionDto Presets,
    CaptionSettingsDto Captions,
    ContentIdeaConfigDto IdeaConfig,
    string Step,
    IReadOnlyList<string> ReachableSteps,
    WizardProgressDto Progress,
    ScriptResponse? Script,
    IReadOnlyList<WizardClipDto> Clips,
    AssetReferenceSlotsDto References,
    string? FinalVideoUrl,
    string? CaptionPreviewUrl,
    bool HasBackgroundMusic,
    bool GoogleFlowAvailable,
    GenerationEstimate Estimate,
    WizardCreditSummaryDto Credits,
    IReadOnlyList<WizardAttemptDto> Attempts,
    WizardCompositionStatusDto Composition,
    WizardValidationDto LastValidation,
    WizardQaDto? Qa,
    IReadOnlyList<string> Blockers,
    bool Failed);
