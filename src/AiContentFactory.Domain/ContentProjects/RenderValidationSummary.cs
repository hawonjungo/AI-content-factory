namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// The outcome of the last final-video validation, persisted as jsonb on
/// <see cref="ContentProject"/> so the wizard's Preview/Export steps can show
/// exactly why a render passed or failed instead of a bare "Failed" badge.
///
/// Errors and warnings are stored as newline-joined scalar strings rather than
/// list properties: EF Core's owned-JSON mapping only picks up scalars, so a
/// <c>List&lt;string&gt;</c> here would silently never persist.
///
/// It is a snapshot, not a history - each render overwrites it.
/// </summary>
public class RenderValidationSummary
{
    private const char Separator = '\n';

    public bool Ok { get; private set; }
    public string Summary { get; private set; } = string.Empty;

    /// <summary>Newline-joined validation errors. Use <see cref="Errors"/> for the split list.</summary>
    public string ErrorsText { get; private set; } = string.Empty;

    /// <summary>Newline-joined validation warnings. Use <see cref="Warnings"/> for the split list.</summary>
    public string WarningsText { get; private set; } = string.Empty;

    public double DurationSeconds { get; private set; }
    public DateTimeOffset? CheckedAt { get; private set; }

    private RenderValidationSummary()
    {
        // EF Core / JSON
    }

    public IReadOnlyList<string> Errors => Split(ErrorsText);
    public IReadOnlyList<string> Warnings => Split(WarningsText);

    /// <summary>No render has been validated yet.</summary>
    public static RenderValidationSummary None() => new();

    public static RenderValidationSummary Create(
        bool ok,
        string summary,
        IEnumerable<string> errors,
        IEnumerable<string> warnings,
        double durationSeconds) => new()
    {
        Ok = ok,
        Summary = summary ?? string.Empty,
        ErrorsText = Join(errors),
        WarningsText = Join(warnings),
        DurationSeconds = Math.Max(0, durationSeconds),
        CheckedAt = DateTimeOffset.UtcNow
    };

    public bool HasRun => CheckedAt is not null;

    private static string Join(IEnumerable<string>? values) =>
        string.Join(Separator, (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Replace('\n', ' ')));

    private static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
