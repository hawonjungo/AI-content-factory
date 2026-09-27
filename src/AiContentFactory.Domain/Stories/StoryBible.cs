namespace AiContentFactory.Domain.Stories;

/// <summary>
/// The reusable "story bible" for a <see cref="Story"/> - written ONCE (see
/// the AI Pipeline's StoryPlannerAgent) before any episode is planned, and
/// referenced by every future episode's planning/writing/validation step for
/// internal consistency. Owned type, persisted as jsonb on the Story row
/// (mirrors <see cref="StoryStateSnapshot"/>'s shape/reasoning).
///
/// List-shaped fields are stored newline-joined rather than as
/// <c>List&lt;string&gt;</c>, same reasoning documented on
/// <see cref="StoryStateSnapshot"/>: EF Core's owned-JSON
/// (<c>OwnsOne(...).ToJson()</c>) mapping here only reliably persists scalar
/// properties.
/// </summary>
public class StoryBible
{
    private const char Separator = '\n';

    /// <summary>Overall story synopsis/logline - a fuller version than <see cref="Story.Premise"/>.</summary>
    public string? Premise { get; private set; }

    /// <summary>Newline-joined world rules. Use <see cref="WorldRules"/> for the split list.</summary>
    public string WorldRulesText { get; private set; } = string.Empty;

    /// <summary>Free-form paragraph describing the recurring cast's identity/voice - complements the structured <see cref="StoryCharacter"/> rows, does not replace them.</summary>
    public string? CharacterDefinitions { get; private set; }

    /// <summary>Free-form paragraph describing how the recurring cast relates to each other.</summary>
    public string? CharacterRelationships { get; private set; }

    /// <summary>Newline-joined visual consistency rules. Use <see cref="VisualConsistencyRules"/> for the split list.</summary>
    public string VisualConsistencyRulesText { get; private set; } = string.Empty;

    /// <summary>The dominant tone/mood, as a short free-form description.</summary>
    public string? Tone { get; private set; }

    /// <summary>Newline-joined recurring motifs/props/phrases. Use <see cref="RecurringElements"/> for the split list.</summary>
    public string RecurringElementsText { get; private set; } = string.Empty;

    /// <summary>Newline-joined hard constraints the story must never violate. Use <see cref="StoryConstraints"/> for the split list.</summary>
    public string StoryConstraintsText { get; private set; } = string.Empty;

    /// <summary>Optional overarching multi-episode arc; null when the story is meant to be open-ended/episodic.</summary>
    public string? StoryArc { get; private set; }

    public IReadOnlyList<string> WorldRules => Split(WorldRulesText);
    public IReadOnlyList<string> VisualConsistencyRules => Split(VisualConsistencyRulesText);
    public IReadOnlyList<string> RecurringElements => Split(RecurringElementsText);
    public IReadOnlyList<string> StoryConstraints => Split(StoryConstraintsText);

    private StoryBible()
    {
        // EF Core / JSON
    }

    public static StoryBible Create(
        string? premise,
        IEnumerable<string>? worldRules,
        string? characterDefinitions,
        string? characterRelationships,
        IEnumerable<string>? visualConsistencyRules,
        string? tone,
        IEnumerable<string>? recurringElements,
        IEnumerable<string>? storyConstraints,
        string? storyArc) => new()
    {
        Premise = Normalize(premise),
        WorldRulesText = Join(worldRules),
        CharacterDefinitions = Normalize(characterDefinitions),
        CharacterRelationships = Normalize(characterRelationships),
        VisualConsistencyRulesText = Join(visualConsistencyRules),
        Tone = Normalize(tone),
        RecurringElementsText = Join(recurringElements),
        StoryConstraintsText = Join(storyConstraints),
        StoryArc = Normalize(storyArc)
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Join(IEnumerable<string>? values) =>
        string.Join(Separator, (values ?? Array.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Replace('\n', ' ')));

    private static IReadOnlyList<string> Split(string? text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
