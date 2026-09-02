using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Generation;

/// <summary>
/// What a single generation call produced. Deliberately broader than
/// <see cref="AiContentFactory.Domain.Costs.VideoGenerationRecord"/> (a
/// Google-Flow-only credit tally): this is the one ledger row per attempt at
/// turning a storyboard scene into a real asset - image, video clip, or
/// narration audio - carrying the provider/model, the credits it was expected
/// to cost, the credits it actually cost, its lifecycle state, and, on
/// failure, why.
///
/// One scene can have several rows (retries, or image + video + audio for the
/// same scene). The retry cap is enforced by counting the Failed rows for a
/// given <see cref="SceneId"/> + <see cref="Kind"/>, so a scene whose video
/// generation keeps failing stops burning credits instead of looping forever.
/// </summary>
public class GenerationAttempt : BaseEntity
{
    public Guid ContentProjectId { get; private set; }
    public Guid? SceneId { get; private set; }
    public GenerationKind Kind { get; private set; }

    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;

    /// <summary>Model tier for a video attempt ("Fast"/"Lite"); null for image/audio.</summary>
    public string? ModelTier { get; private set; }

    /// <summary>Credits this attempt was expected to draw from the daily budget when it was reserved.</summary>
    public int EstimatedCredits { get; private set; }

    /// <summary>Credits actually charged once the provider call returned. Null until <see cref="Status"/> is Completed/Validated.</summary>
    public int? ActualCredits { get; private set; }

    public GenerationAttemptStatus Status { get; private set; } = GenerationAttemptStatus.Pending;

    /// <summary>1 for the first try at this scene+kind, incremented for each retry.</summary>
    public int AttemptNumber { get; private set; } = 1;

    public string? FailureReason { get; private set; }

    /// <summary>The asset this attempt produced, once it succeeded.</summary>
    public Guid? AssetId { get; private set; }

    /// <summary>For an audio attempt: the validated narration duration. Null otherwise.</summary>
    public double? AudioDurationSeconds { get; private set; }

    private GenerationAttempt()
    {
        // EF Core
    }

    public static GenerationAttempt Reserve(
        Guid contentProjectId,
        Guid? sceneId,
        GenerationKind kind,
        string provider,
        string model,
        string? modelTier,
        int estimatedCredits,
        int attemptNumber)
    {
        if (estimatedCredits < 0)
        {
            throw new DomainException("EstimatedCredits cannot be negative.");
        }

        return new GenerationAttempt
        {
            ContentProjectId = contentProjectId,
            SceneId = sceneId,
            Kind = kind,
            Provider = provider ?? string.Empty,
            Model = model ?? string.Empty,
            ModelTier = string.IsNullOrWhiteSpace(modelTier) ? null : modelTier.Trim(),
            EstimatedCredits = estimatedCredits,
            AttemptNumber = attemptNumber < 1 ? 1 : attemptNumber,
            Status = GenerationAttemptStatus.Pending
        };
    }

    public void MarkGenerating()
    {
        Status = GenerationAttemptStatus.Generating;
        Touch();
    }

    public void MarkRetrying()
    {
        Status = GenerationAttemptStatus.Retrying;
        Touch();
    }

    public void MarkCompleted(int actualCredits, Guid? assetId, double? audioDurationSeconds)
    {
        if (actualCredits < 0)
        {
            throw new DomainException("ActualCredits cannot be negative.");
        }

        ActualCredits = actualCredits;
        AssetId = assetId;
        AudioDurationSeconds = audioDurationSeconds;
        Status = GenerationAttemptStatus.Completed;
        FailureReason = null;
        Touch();
    }

    /// <summary>
    /// Post-generation checks passed (e.g. narration audio exists, is
    /// readable, and is not silent). Only a Completed attempt can be validated.
    /// </summary>
    public void MarkValidated()
    {
        if (Status != GenerationAttemptStatus.Completed && Status != GenerationAttemptStatus.Validated)
        {
            throw new DomainException($"Only a Completed attempt can be validated (was '{Status}').");
        }

        Status = GenerationAttemptStatus.Validated;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "unknown error" : reason.Trim();
        // A provider call that threw was not billed, so a failed attempt keeps
        // ActualCredits null - the credit ledger only counts reserved/charged
        // credits, never failed ones, against the daily budget.
        Status = GenerationAttemptStatus.Failed;
        Touch();
    }
}
