using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// Aggregate root for a single piece of content moving through the
/// script -> QA gate -> clip plan -> generate clips -> render -> approve pipeline.
///
/// QA deliberately runs right after script generation (ScriptReady -> QA),
/// BEFORE any Veo spend happens - scoring a finished render can't undo
/// already-spent generation cost, so the gate that can actually save money
/// has to sit before StoryboardReady, not after Editing.
/// </summary>
public class ContentProject : BaseEntity
{
    private static readonly Dictionary<ContentProjectStatus, ContentProjectStatus[]> AllowedTransitions = new()
    {
        // Draft/ScriptReady -> Generating is for the Google Flow pipeline,
        // which writes its own hook script and so does not need the reviewed
        // script or a clip plan. The standard pipeline still gates itself on
        // "has a clip plan", so this doesn't let it start prematurely.
        [ContentProjectStatus.Draft] = new[] { ContentProjectStatus.Researching, ContentProjectStatus.ScriptReady, ContentProjectStatus.Generating, ContentProjectStatus.Failed },
        [ContentProjectStatus.Researching] = new[] { ContentProjectStatus.ScriptReady, ContentProjectStatus.Failed },
        [ContentProjectStatus.ScriptReady] = new[] { ContentProjectStatus.QA, ContentProjectStatus.StoryboardReady, ContentProjectStatus.Generating, ContentProjectStatus.Failed },
        [ContentProjectStatus.QA] = new[] { ContentProjectStatus.StoryboardReady, ContentProjectStatus.ScriptReady, ContentProjectStatus.Failed },
        [ContentProjectStatus.StoryboardReady] = new[] { ContentProjectStatus.Generating, ContentProjectStatus.StoryboardReady, ContentProjectStatus.Failed },
        [ContentProjectStatus.Generating] = new[] { ContentProjectStatus.Editing, ContentProjectStatus.Failed },
        // Editing/AwaitingApproval -> Generating is what makes "regenerate
        // just this one clip" legal: you've already got a full set of clips
        // (or even a finished render) and want to replace one of them without
        // starting the project over.
        [ContentProjectStatus.Editing] = new[] { ContentProjectStatus.AwaitingApproval, ContentProjectStatus.Editing, ContentProjectStatus.Generating, ContentProjectStatus.Failed },
        [ContentProjectStatus.AwaitingApproval] = new[] { ContentProjectStatus.Approved, ContentProjectStatus.Rejected, ContentProjectStatus.Editing, ContentProjectStatus.Generating },
        [ContentProjectStatus.Approved] = new[] { ContentProjectStatus.Published, ContentProjectStatus.Failed },
        [ContentProjectStatus.Rejected] = new[] { ContentProjectStatus.Editing, ContentProjectStatus.Draft },
        [ContentProjectStatus.Published] = Array.Empty<ContentProjectStatus>(),
        // Failed has to be recoverable in place. It only allowed Draft, which
        // meant one failed render left the project permanently unable to
        // re-render or regenerate a clip - every retry threw on the transition,
        // even though the clips and script were still perfectly good.
        [ContentProjectStatus.Failed] = new[] { ContentProjectStatus.Draft, ContentProjectStatus.Editing, ContentProjectStatus.Generating }
    };

    public string Title { get; private set; } = string.Empty;
    public string? Topic { get; private set; }
    public string? Niche { get; private set; }
    public ContentProjectStatus Status { get; private set; } = ContentProjectStatus.Draft;
    public int TargetDurationSeconds { get; private set; }
    public string AspectRatio { get; private set; } = "9:16";
    public string Language { get; private set; } = "en";

    /// <summary>
    /// Ids into the code-defined preset catalog (Application/Presets). Only
    /// the ids are stored: style and voice presets are re-resolved from the
    /// catalog at generation time so catalog fixes reach existing projects,
    /// while caption values are snapshotted into <see cref="Captions"/>
    /// because the user edits them directly after picking a preset.
    /// </summary>
    public string? TemplateId { get; private set; }
    public string? StylePresetId { get; private set; }
    public string? VoicePresetId { get; private set; }
    public string? CaptionPresetId { get; private set; }

    public CaptionSettings Captions { get; private set; } = CaptionSettings.Default();

    /// <summary>
    /// Step 6 audio handling. New projects default to <see cref="Domain.ContentProjects.AudioMode.Smart"/>
    /// (per clip: keep original audio where present, TTS only for the gaps);
    /// projects that predate the feature were migrated to
    /// <see cref="Domain.ContentProjects.AudioMode.Generated"/> so their behaviour did not change.
    /// </summary>
    public AudioMode AudioMode { get; private set; } = AudioMode.Smart;

    /// <summary>Step 2 idea configuration (narrative direction, voice, credit strategy). jsonb column.</summary>
    public ContentIdeaConfig IdeaConfig { get; private set; } = ContentIdeaConfig.Default();

    /// <summary>Outcome of the most recent final-video validation. jsonb column.</summary>
    public RenderValidationSummary LastRenderValidation { get; private set; } = RenderValidationSummary.None();

    public GenerationProgress Progress { get; private set; } = GenerationProgress.Idle();

    private ContentProject()
    {
        // EF Core
    }

    public static ContentProject Create(string title, string? topic, string? niche, int targetDurationSeconds, string aspectRatio, string language)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (targetDurationSeconds is < 1 or > 180)
        {
            throw new DomainException("TargetDurationSeconds must be between 1 and 180 seconds for short-form content.");
        }

        return new ContentProject
        {
            Title = title.Trim(),
            Topic = topic?.Trim(),
            Niche = niche?.Trim(),
            TargetDurationSeconds = targetDurationSeconds,
            AspectRatio = string.IsNullOrWhiteSpace(aspectRatio) ? "9:16" : aspectRatio.Trim(),
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim()
        };
    }

    public void UpdateDetails(string title, string? topic, string? niche, int targetDurationSeconds)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (targetDurationSeconds is < 1 or > 180)
        {
            throw new DomainException("TargetDurationSeconds must be between 1 and 180 seconds for short-form content.");
        }

        Title = title.Trim();
        Topic = topic?.Trim();
        Niche = niche?.Trim();
        TargetDurationSeconds = targetDurationSeconds;
        Touch();
    }

    /// <summary>
    /// Records which presets the project was built with. Pass null for any
    /// slot you don't want to change - re-picking only the voice shouldn't
    /// wipe the caption styling the user already tuned.
    /// </summary>
    public void ApplyPresets(string? templateId, string? stylePresetId, string? voicePresetId, string? captionPresetId, CaptionSettings? captionSettings)
    {
        TemplateId = Normalize(templateId) ?? TemplateId;
        StylePresetId = Normalize(stylePresetId) ?? StylePresetId;
        VoicePresetId = Normalize(voicePresetId) ?? VoicePresetId;

        if (Normalize(captionPresetId) is { } newCaptionPresetId)
        {
            CaptionPresetId = newCaptionPresetId;

            // Picking a caption preset is the one action that intentionally
            // discards manual caption edits - that's what "apply a preset"
            // means to the user.
            if (captionSettings is not null)
            {
                Captions = captionSettings;
            }
        }

        Touch();
    }

    public void UpdateCaptions(CaptionSettings captions)
    {
        Captions = captions ?? throw new DomainException("Caption settings are required.");
        Touch();
    }

    /// <summary>Sets the Step 6 audio source (keep original clip audio, generate a new voice-over, or mute).</summary>
    public void SetAudioMode(AudioMode audioMode)
    {
        AudioMode = audioMode;
        Touch();
    }

    /// <summary>Picks the narration voice preset. Null / blank clears it back to "use the template's default".</summary>
    public void SetVoicePreset(string? voicePresetId)
    {
        VoicePresetId = Normalize(voicePresetId);
        Touch();
    }

    public void UpdateIdeaConfig(ContentIdeaConfig ideaConfig)
    {
        IdeaConfig = ideaConfig ?? throw new DomainException("Idea configuration is required.");
        Touch();
    }

    /// <summary>Records the result of validating the finished MP4 - overwrites the previous run's result.</summary>
    public void RecordRenderValidation(RenderValidationSummary summary)
    {
        LastRenderValidation = summary ?? RenderValidationSummary.None();
        Touch();
    }

    /// <summary>
    /// Overwrites the previous progress report - this is a "where are we right
    /// now" field, not a history. Safe to call from inside a generation loop.
    /// </summary>
    public void ReportProgress(string stage, int completedUnits, int totalUnits, string? message = null)
    {
        Progress = GenerationProgress.Create(stage, completedUnits, totalUnits, message);
        Touch();
    }

    public void ClearProgress()
    {
        Progress = GenerationProgress.Idle();
        Touch();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void TransitionTo(ContentProjectStatus newStatus)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new DomainException($"Cannot transition ContentProject from '{Status}' to '{newStatus}'.");
        }

        Status = newStatus;
        Touch();
    }

    /// <summary>
    /// Same as TransitionTo, but a no-op if already at newStatus - for
    /// actions that are meant to be repeatable (re-render, editing the clip
    /// plan again) without the caller needing to special-case "already there".
    /// </summary>
    public void TransitionToIfNeeded(ContentProjectStatus newStatus)
    {
        if (Status != newStatus)
        {
            TransitionTo(newStatus);
        }
    }
}
