using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Stories.Continuity;

/// <summary>
/// Closed set of top-level continuity issue categories. Every
/// <see cref="ContinuityIssue"/> the ContinuityValidatorAgent prompt reports
/// must use exactly one of these (see <see cref="ContinuityIssueTypes"/> for
/// the closed set of specific "type" values nested under each category).
/// </summary>
public static class ContinuityCategories
{
    public const string Character = "Character";
    public const string Location = "Location";
    public const string Timeline = "Timeline";
    public const string Object = "Object";
    public const string Plot = "Plot";

    /// <summary>Escape hatch for a genuine violation that doesn't fit any of the five domain categories above - always paired with <see cref="ContinuityIssueTypes.Other"/>.</summary>
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[] { Character, Location, Timeline, Object, Plot, Other };
}

/// <summary>
/// Closed set of specific continuity violation subtypes the
/// ContinuityValidatorAgent prompt instructs the model to choose from,
/// grouped by <see cref="ContinuityCategories"/>, so issues have a
/// consistent, machine-checkable shape instead of arbitrary free-form
/// casing/values.
/// </summary>
public static class ContinuityIssueTypes
{
    // Character
    public const string MissingCharacter = "MISSING_CHARACTER";
    public const string PersonalityContradiction = "PERSONALITY_CONTRADICTION";
    public const string ImpossibleAction = "IMPOSSIBLE_ACTION";
    public const string UnexplainedRelationshipChange = "UNEXPLAINED_RELATIONSHIP_CHANGE";

    // Location
    public const string ImpossibleLocationTransition = "IMPOSSIBLE_LOCATION_TRANSITION";
    public const string UnexplainedTravel = "UNEXPLAINED_TRAVEL";
    public const string InconsistentLocationDescription = "INCONSISTENT_LOCATION_DESCRIPTION";

    // Timeline
    public const string ImpossibleEventOrder = "IMPOSSIBLE_EVENT_ORDER";
    public const string DuplicateEvent = "DUPLICATE_EVENT";
    public const string ImpossibleTimeJump = "IMPOSSIBLE_TIME_JUMP";

    // Object
    public const string ObjectOwnershipConflict = "OBJECT_OWNERSHIP_CONFLICT";
    public const string ObjectLocationConflict = "OBJECT_LOCATION_CONFLICT";
    public const string ObjectStatusConflict = "OBJECT_STATUS_CONFLICT";

    // Plot
    public const string UnresolvedThreadForgotten = "UNRESOLVED_THREAD_FORGOTTEN";
    public const string ObjectiveRepeated = "OBJECTIVE_REPEATED";
    public const string PreviousEpisodeContradiction = "PREVIOUS_EPISODE_CONTRADICTION";
    public const string UnexplainedMajorEvent = "UNEXPLAINED_MAJOR_EVENT";

    // Other (escape hatch - always paired with category "Other")
    public const string Other = "OTHER";

    /// <summary>Category -&gt; allowed types, in prompt/display order. Drives both the system prompt's closed-set instruction and (indirectly) the taxonomy documented on <see cref="ContinuityCategories"/>.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByCategory = new Dictionary<string, IReadOnlyList<string>>
    {
        [ContinuityCategories.Character] = new[] { MissingCharacter, PersonalityContradiction, ImpossibleAction, UnexplainedRelationshipChange },
        [ContinuityCategories.Location] = new[] { ImpossibleLocationTransition, UnexplainedTravel, InconsistentLocationDescription },
        [ContinuityCategories.Timeline] = new[] { ImpossibleEventOrder, DuplicateEvent, ImpossibleTimeJump },
        [ContinuityCategories.Object] = new[] { ObjectOwnershipConflict, ObjectLocationConflict, ObjectStatusConflict },
        [ContinuityCategories.Plot] = new[] { UnresolvedThreadForgotten, ObjectiveRepeated, PreviousEpisodeContradiction, UnexplainedMajorEvent },
        [ContinuityCategories.Other] = new[] { Other },
    };
}

/// <param name="StoryTitle">The parent Story's title.</param>
/// <param name="BibleSummary">Truncated flattened Story Bible.</param>
/// <param name="EpisodeContent">The episode's script (preferred) or, if no script exists yet, its outline - truncated.</param>
public record ContinuityValidatorAgentInput(
    string StoryTitle,
    string BibleSummary,
    string EpisodeContent,
    string? CurrentLocation,
    string? CurrentObjective,
    string? CharacterStates,
    IReadOnlyList<string>? OpenStoryThreads,
    IReadOnlyList<string>? UnresolvedConflicts,
    IReadOnlyList<string>? KnownFacts);

public record ContinuityIssue(string Category, string Type, string Message);

/// <summary>
/// Raw LLM output. Deliberately has NO "IsValid"/"Valid" field: whether the
/// episode is valid is a structural fact (are there any CriticalIssues?),
/// not something the model should self-report and risk disagreeing with its
/// own issue list - callers (<see cref="StoryContinuityDtos.ContinuityValidationResponse"/>)
/// compute it deterministically instead. <see cref="Score"/> is the model's
/// own 0.0-1.0 self-assessed continuity-consistency score for the episode as
/// a whole - not clamped here, callers clamp defensively when mapping out.
/// </summary>
public record ContinuityValidationOutput(List<ContinuityIssue>? Warnings, List<ContinuityIssue>? CriticalIssues, double Score);

public interface IContinuityValidatorAgent
{
    Task<ContinuityValidationOutput> GenerateAsync(ContinuityValidatorAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only continuity check for one episode against the Story Bible and
/// current canonical Story State - never mutates anything itself.
/// <see cref="StoryContinuityManager"/> calls this both standalone
/// (ValidateEpisodeAsync) and as the gate before FinalizeEpisodeAsync ever
/// touches StoryState.
/// </summary>
public class ContinuityValidatorAgent : IContinuityValidatorAgent
{
    private static readonly string TaxonomyText = string.Join("\n", ContinuityIssueTypes.ByCategory
        .Select(kv => $"  - {kv.Key}: {string.Join(", ", kv.Value)}"));

    private static readonly string SystemPrompt = $$"""
        You are a continuity QA checker for an ongoing multi-episode
        short-form video series. You are given one episode's content, the
        series' Story Bible, and the current canonical continuity state. Run
        through every check below and report any violation you find as an
        issue.

        For every issue you report, choose:
        - "category": EXACTLY one of: {{string.Join(", ", ContinuityCategories.All)}}
        - "type": EXACTLY one of the allowed types for that category (use the
          exact strings, no other casing or values). Allowed types per category:
        {{TaxonomyText}}
        - "message": a short, specific explanation of the violation.

        Checks to run, grouped by category:

        Character
        1. Missing character - a character the scene clearly needs (per the
           Bible/state/continuity requirements) is absent with no explanation.
           -> MISSING_CHARACTER
        2. Personality contradiction - a character acts against an established
           personality trait or role with no narrative reason. -> PERSONALITY_CONTRADICTION
        3. Impossible action - a character does something they should be
           incapable of given an established injury, skill limit, or
           constraint. -> IMPOSSIBLE_ACTION
        4. Unexplained relationship change - a relationship between characters
           shifts (ally to enemy, stranger to friend, etc.) with no
           explanation. -> UNEXPLAINED_RELATIONSHIP_CHANGE

        Location
        5. Impossible location transition - a character is in a different
           place than established with no plausible way to have gotten there
           (a "teleport"). -> IMPOSSIBLE_LOCATION_TRANSITION
        6. Unexplained travel - a location change happens but is never
           narrated or explained, even if not physically impossible. -> UNEXPLAINED_TRAVEL
        7. Inconsistent location description - the same location is described
           in a way that contradicts its established description. -> INCONSISTENT_LOCATION_DESCRIPTION

        Timeline
        8. Impossible event order - events happen in an order that
           contradicts established sequence or causality. -> IMPOSSIBLE_EVENT_ORDER
        9. Duplicate event - an event that already canonically happened is
           narrated again as if it's new. -> DUPLICATE_EVENT
        10. Impossible time jump - the episode's time progression contradicts
            the established timeline (too fast, too slow, or backwards)
            without acknowledgement. -> IMPOSSIBLE_TIME_JUMP

        Object
        11. Object ownership conflict - who possesses an item contradicts its
            established ownership. -> OBJECT_OWNERSHIP_CONFLICT
        12. Object location conflict - where an item is contradicts its
            established location. -> OBJECT_LOCATION_CONFLICT
        13. Object status conflict - an item's condition (broken, lost,
            hidden, given away, etc.) contradicts its established status. -> OBJECT_STATUS_CONFLICT

        Plot
        14. Unresolved thread forgotten - an open story thread that had a
            natural opportunity to be addressed is dropped without
            acknowledgement (not every thread must be resolved, but it must
            not simply vanish). -> UNRESOLVED_THREAD_FORGOTTEN
        15. Objective repeated - a goal already achieved or abandoned is
            treated as new or still unaddressed. -> OBJECTIVE_REPEATED
        16. Previous episode contradiction - this episode directly
            contradicts something established as true by the previous
            episode's ending. -> PREVIOUS_EPISODE_CONTRADICTION
        17. Unexplained major event - a significant plot event (death, major
            reveal, relationship change, etc.) is presented with no setup or
            explanation. -> UNEXPLAINED_MAJOR_EVENT

        If a genuine violation doesn't fit any check above, report it with
        category "Other" and type "OTHER".

        If nothing is given to compare against (e.g. the state is empty
        because this is genuinely the first episode), that is not itself a
        violation.

        Severity - decide, for every issue you find, whether it is a
        CRITICAL issue or a lower-severity WARNING. This is a judgment call;
        use these examples to calibrate:

        Critical (blocks treating the episode as canon):
        - Character: the established coward acts as a fearless leader with no
          in-story reason (flat contradiction of a load-bearing trait).
        - Location: a character is in a different city/building with zero
          travel time or explanation (a hard teleport).
        - Plot: a goal the current state already marks completed is written
          as if newly unresolved; or the episode directly contradicts a fact
          stated as true at the end of the previous episode.

        Warning (worth flagging, should NOT by itself block finalizing):
        - Character: a minor personality inconsistency that doesn't break the
          character's core identity.
        - Plot: an open thread isn't addressed this episode but hasn't been
          dropped long enough to call it abandoned yet.
        - Location/Object: a small descriptive wording inconsistency that
          doesn't change meaning (e.g. "red backpack" vs "crimson backpack").

        Also self-assess an overall continuity-consistency "score" for the
        episode as a whole, from 0.0 (severely broken) to 1.0 (fully
        consistent).

        Always respond with ONLY a single JSON object, no markdown fences, no
        commentary, matching exactly this schema:
        {"warnings": [{"category": string, "type": string, "message": string}], "criticalIssues": [{"category": string, "type": string, "message": string}], "score": number}

        - warnings / criticalIssues: empty arrays when there is nothing to
          report in that bucket.
        - Do NOT include an "isValid" or "valid" field - it is not part of
          this schema and whether the episode is valid is computed
          separately from criticalIssues.
        """;

    private readonly ILlmRouter _router;

    public ContinuityValidatorAgent(ILlmRouter router)
    {
        _router = router;
    }

    public Task<ContinuityValidationOutput> GenerateAsync(ContinuityValidatorAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Series: {input.StoryTitle}

            Story Bible:
            {input.BibleSummary}

            Current canonical state (before this episode):
            - Current location: {Or(input.CurrentLocation)}
            - Current objective: {Or(input.CurrentObjective)}
            - Character states: {Or(input.CharacterStates)}
            - Open story threads: {OrList(input.OpenStoryThreads)}
            - Unresolved conflicts: {OrList(input.UnresolvedConflicts)}
            - Known facts: {OrList(input.KnownFacts)}

            Episode content to check:
            {input.EpisodeContent}

            Run the continuity check now.
            """;

        return JsonAgentRunner.RunAsync<ContinuityValidationOutput>(
            _router.Resolve(LlmTaskType.Script),
            SystemPrompt,
            userPrompt,
            validate: output => (output.Warnings ?? new List<ContinuityIssue>())
                .Concat(output.CriticalIssues ?? new List<ContinuityIssue>())
                .All(IsWellFormedIssue),
            cancellationToken);
    }

    /// <summary>Every issue must carry a non-empty Category/Type/Message, and Category must be one of the closed set the prompt offered.</summary>
    private static bool IsWellFormedIssue(ContinuityIssue issue) =>
        !string.IsNullOrWhiteSpace(issue.Category) &&
        !string.IsNullOrWhiteSpace(issue.Type) &&
        !string.IsNullOrWhiteSpace(issue.Message) &&
        ContinuityCategories.All.Contains(issue.Category, StringComparer.OrdinalIgnoreCase);

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(not specified)" : value.Trim();

    private static string OrList(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 ? "(none)" : string.Join("; ", values);
}
