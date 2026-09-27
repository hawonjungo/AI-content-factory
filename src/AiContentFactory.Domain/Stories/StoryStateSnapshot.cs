namespace AiContentFactory.Domain.Stories;

/// <summary>
/// A point-in-time copy of a <see cref="StoryState"/>'s fields, captured when
/// a <see cref="StoryEpisode"/> is completed via <see cref="StoryEpisode.Complete"/>.
/// Not a live reference - editing the Story's current <see cref="StoryState"/>
/// afterwards does not change past snapshots. Persisted as jsonb, owned by the
/// episode.
///
/// List-shaped fields are stored newline-joined rather than as
/// <c>List&lt;string&gt;</c>, same reasoning documented on
/// <see cref="AiContentFactory.Domain.ContentProjects.RenderValidationSummary"/>:
/// EF Core's owned-JSON (<c>OwnsOne(...).ToJson()</c>) mapping here only
/// reliably persists scalar properties.
/// </summary>
public class StoryStateSnapshot
{
    private const char Separator = '\n';

    public string? CurrentLocation { get; private set; }
    public string? CurrentObjective { get; private set; }

    /// <summary>Free-form notes on where each recurring character currently stands.</summary>
    public string? CharacterStates { get; private set; }

    /// <summary>Newline-joined events. Use <see cref="ImportantEvents"/> for the split list.</summary>
    public string ImportantEventsText { get; private set; } = string.Empty;

    /// <summary>Newline-joined threads. Use <see cref="OpenStoryThreads"/> for the split list.</summary>
    public string OpenStoryThreadsText { get; private set; } = string.Empty;

    /// <summary>Newline-joined conflicts. Use <see cref="UnresolvedConflicts"/> for the split list.</summary>
    public string UnresolvedConflictsText { get; private set; } = string.Empty;

    /// <summary>Newline-joined facts. Use <see cref="KnownFacts"/> for the split list.</summary>
    public string KnownFactsText { get; private set; } = string.Empty;

    public string? NextPlannedDestination { get; private set; }

    /// <summary>Catch-all for other continuity information.</summary>
    public string? Notes { get; private set; }

    private StoryStateSnapshot()
    {
        // EF Core / JSON
    }

    public IReadOnlyList<string> ImportantEvents => Split(ImportantEventsText);
    public IReadOnlyList<string> OpenStoryThreads => Split(OpenStoryThreadsText);
    public IReadOnlyList<string> UnresolvedConflicts => Split(UnresolvedConflictsText);
    public IReadOnlyList<string> KnownFacts => Split(KnownFactsText);

    public static StoryStateSnapshot Create(
        string? currentLocation,
        string? currentObjective,
        string? characterStates,
        IEnumerable<string>? importantEvents,
        IEnumerable<string>? openStoryThreads,
        IEnumerable<string>? unresolvedConflicts,
        IEnumerable<string>? knownFacts,
        string? nextPlannedDestination,
        string? notes) => new()
    {
        CurrentLocation = Normalize(currentLocation),
        CurrentObjective = Normalize(currentObjective),
        CharacterStates = Normalize(characterStates),
        ImportantEventsText = Join(importantEvents),
        OpenStoryThreadsText = Join(openStoryThreads),
        UnresolvedConflictsText = Join(unresolvedConflicts),
        KnownFactsText = Join(knownFacts),
        NextPlannedDestination = Normalize(nextPlannedDestination),
        Notes = Normalize(notes)
    };

    /// <summary>Freezes the current fields of a live <see cref="StoryState"/> into a snapshot.</summary>
    public static StoryStateSnapshot FromState(StoryState state) => Create(
        state.CurrentLocation,
        state.CurrentObjective,
        state.CharacterStates,
        state.ImportantEvents,
        state.OpenStoryThreads,
        state.UnresolvedConflicts,
        state.KnownFacts,
        state.NextPlannedDestination,
        state.Notes);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Join(IEnumerable<string>? values) =>
        string.Join(Separator, (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Replace('\n', ' ')));

    private static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
