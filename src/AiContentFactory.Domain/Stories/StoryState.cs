using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.Stories;

/// <summary>
/// The current canonical continuity state of a <see cref="Story"/> - what a
/// future script-writing agent needs to keep episode N+1 consistent with
/// everything that happened before. 1:1 with <see cref="Story"/>; each
/// completed <see cref="StoryEpisode"/> takes a <see cref="StoryStateSnapshot"/>
/// copy of these fields for history, but this row is always "where the story
/// is right now". A real entity (own table, own identity) rather than an
/// owned type on <see cref="Story"/>, since it has an independent lifecycle
/// and its shape is reused by <see cref="StoryStateSnapshot"/>.
///
/// List-shaped fields are stored newline-joined, same reasoning documented on
/// <see cref="StoryStateSnapshot"/>.
/// </summary>
public class StoryState : BaseEntity
{
    private const char Separator = '\n';

    public Guid StoryId { get; private set; }

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

    /// <summary>Catch-all for other continuity information that doesn't fit the fields above.</summary>
    public string? Notes { get; private set; }

    public IReadOnlyList<string> ImportantEvents => Split(ImportantEventsText);
    public IReadOnlyList<string> OpenStoryThreads => Split(OpenStoryThreadsText);
    public IReadOnlyList<string> UnresolvedConflicts => Split(UnresolvedConflictsText);
    public IReadOnlyList<string> KnownFacts => Split(KnownFactsText);

    private StoryState()
    {
        // EF Core
    }

    public static StoryState Create(Guid storyId) => new()
    {
        StoryId = storyId
    };

    /// <summary>Replaces every field wholesale - no partial-field diffing.</summary>
    public void Update(
        string? currentLocation,
        string? currentObjective,
        string? characterStates,
        IEnumerable<string>? importantEvents,
        IEnumerable<string>? openStoryThreads,
        IEnumerable<string>? unresolvedConflicts,
        IEnumerable<string>? knownFacts,
        string? nextPlannedDestination,
        string? notes)
    {
        CurrentLocation = Normalize(currentLocation);
        CurrentObjective = Normalize(currentObjective);
        CharacterStates = Normalize(characterStates);
        ImportantEventsText = Join(importantEvents);
        OpenStoryThreadsText = Join(openStoryThreads);
        UnresolvedConflictsText = Join(unresolvedConflicts);
        KnownFactsText = Join(knownFacts);
        NextPlannedDestination = Normalize(nextPlannedDestination);
        Notes = Normalize(notes);
        Touch();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Join(IEnumerable<string>? values) =>
        string.Join(Separator, (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Replace('\n', ' ')));

    private static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
