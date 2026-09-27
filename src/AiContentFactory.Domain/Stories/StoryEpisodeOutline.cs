namespace AiContentFactory.Domain.Stories;

/// <summary>
/// The production plan for one <see cref="StoryEpisode"/>, written by the AI
/// Pipeline's EpisodePlannerAgent before the script itself is written. Owned
/// type, persisted as jsonb on the StoryEpisode row (mirrors
/// <see cref="StoryStateSnapshot"/>'s shape/reasoning).
///
/// List-shaped fields are stored newline-joined rather than as
/// <c>List&lt;string&gt;</c>, same reasoning documented on
/// <see cref="StoryStateSnapshot"/>.
/// </summary>
public class StoryEpisodeOutline
{
    private const char Separator = '\n';

    /// <summary>Working title for the episode, may differ from <see cref="StoryEpisode.Title"/>.</summary>
    public string? Title { get; private set; }

    public string? Objective { get; private set; }
    public string? Setup { get; private set; }

    /// <summary>Newline-joined major plot beats, in order. Use <see cref="MajorBeats"/> for the split list.</summary>
    public string MajorBeatsText { get; private set; } = string.Empty;

    public string? Conflict { get; private set; }
    public string? Escalation { get; private set; }
    public string? Resolution { get; private set; }
    public string? Cliffhanger { get; private set; }

    /// <summary>Newline-joined continuity items the script must honour (facts/threads from <see cref="StoryState"/> that this episode must not contradict). Use <see cref="ContinuityRequirements"/> for the split list.</summary>
    public string ContinuityRequirementsText { get; private set; } = string.Empty;

    /// <summary>Newline-joined short descriptions of the scenes required to cover this outline. Use <see cref="ScenesRequired"/> for the split list.</summary>
    public string ScenesRequiredText { get; private set; } = string.Empty;

    public IReadOnlyList<string> MajorBeats => Split(MajorBeatsText);
    public IReadOnlyList<string> ContinuityRequirements => Split(ContinuityRequirementsText);
    public IReadOnlyList<string> ScenesRequired => Split(ScenesRequiredText);

    private StoryEpisodeOutline()
    {
        // EF Core / JSON
    }

    public static StoryEpisodeOutline Create(
        string? title,
        string? objective,
        string? setup,
        IEnumerable<string>? majorBeats,
        string? conflict,
        string? escalation,
        string? resolution,
        string? cliffhanger,
        IEnumerable<string>? continuityRequirements,
        IEnumerable<string>? scenesRequired) => new()
    {
        Title = Normalize(title),
        Objective = Normalize(objective),
        Setup = Normalize(setup),
        MajorBeatsText = Join(majorBeats),
        Conflict = Normalize(conflict),
        Escalation = Normalize(escalation),
        Resolution = Normalize(resolution),
        Cliffhanger = Normalize(cliffhanger),
        ContinuityRequirementsText = Join(continuityRequirements),
        ScenesRequiredText = Join(scenesRequired)
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Join(IEnumerable<string>? values) =>
        string.Join(Separator, (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Replace('\n', ' ')));

    private static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
