namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// Coarse "how far along is the long-running job" state, owned by the
/// ContentProject and persisted as jsonb.
///
/// This exists because ContentProjectStatus alone can't answer the only
/// question a user actually has while waiting: clip generation sits in
/// <see cref="ContentProjectStatus.Generating"/> for 20+ minutes whether it is
/// on clip 1 of 5 or clip 5 of 5. The wizard reads this to show "Đang dựng
/// clip 3/5" instead of an unmoving spinner.
///
/// Deliberately not an audit log - each report overwrites the last. Hangfire
/// already keeps job history for anyone who needs it.
/// </summary>
public class GenerationProgress
{
    /// <summary>Machine-readable stage key (e.g. "clips", "voice", "render"); the wizard maps it to a label.</summary>
    public string Stage { get; private set; } = IdleStage;

    public int CompletedUnits { get; private set; }

    public int TotalUnits { get; private set; }

    /// <summary>Optional human-readable detail, already phrased for end users.</summary>
    public string? Message { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public const string IdleStage = "idle";

    private GenerationProgress()
    {
        // EF Core / JSON deserialization
    }

    public static GenerationProgress Idle() => new();

    public static GenerationProgress Create(string stage, int completedUnits, int totalUnits, string? message) => new()
    {
        Stage = string.IsNullOrWhiteSpace(stage) ? IdleStage : stage.Trim(),
        CompletedUnits = Math.Max(0, completedUnits),
        TotalUnits = Math.Max(0, totalUnits),
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
        UpdatedAt = DateTimeOffset.UtcNow
    };

    /// <summary>0-100. Returns 0 rather than dividing by zero when the total isn't known yet.</summary>
    public int PercentComplete => TotalUnits <= 0
        ? 0
        : Math.Clamp((int)Math.Round(CompletedUnits * 100.0 / TotalUnits), 0, 100);

    /// <summary>
    /// No stage means nothing is running - treated the same as an explicit
    /// idle. Without the empty check, a row whose jsonb predates this column
    /// deserializes with a null Stage and the wizard shows a permanent
    /// "working..." for a job that isn't there.
    /// </summary>
    public bool IsIdle => string.IsNullOrWhiteSpace(Stage) || Stage == IdleStage;
}
