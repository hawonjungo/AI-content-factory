using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Stories.Continuity;

/// <param name="Title">The Story's title.</param>
/// <param name="Theme">Overall theme, if given (e.g. "found family", "revenge").</param>
/// <param name="Genre">Genre, if given (e.g. "slice of life", "mystery", "horror").</param>
/// <param name="Tone">Desired dominant tone, if given.</param>
/// <param name="TargetAudience">Who this series is for, if given.</param>
/// <param name="MainCharacters">Short "Name: description" lines for the recurring cast - authoritative, never invent additional main characters.</param>
/// <param name="Locations">Short "Name: description" lines for recurring locations/world elements.</param>
/// <param name="StoryRulesConstraints">Free-form hard rules/constraints the user wants enforced.</param>
/// <param name="DesiredEpisodeCount">Null when the story is meant to be open-ended.</param>
public record StoryPlannerAgentInput(
    string Title,
    string? Theme,
    string? Genre,
    string? Tone,
    string? TargetAudience,
    IReadOnlyList<string>? MainCharacters,
    IReadOnlyList<string>? Locations,
    string? StoryRulesConstraints,
    int? DesiredEpisodeCount);

public record StoryPlannerAgentOutput(
    string? Premise,
    List<string>? WorldRules,
    string? CharacterDefinitions,
    string? CharacterRelationships,
    List<string>? VisualConsistencyRules,
    string? Tone,
    List<string>? RecurringElements,
    List<string>? StoryConstraints,
    string? StoryArc);

public interface IStoryPlannerAgent
{
    Task<StoryPlannerAgentOutput> GenerateAsync(StoryPlannerAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes a Story's "bible" ONCE, before any episode is planned - the
/// reusable premise/world/character/visual-consistency reference that every
/// future EpisodePlannerAgent/StoryScriptWriterAgent/ContinuityValidatorAgent
/// call is checked against. Never called per-episode.
/// </summary>
public class StoryPlannerAgent : IStoryPlannerAgent
{
    private const string SystemPrompt = """
        You are a story bible architect for a multi-episode short-form vertical
        video series (TikTok/Reels/Shorts). You write the series' "bible" ONCE,
        before any single episode is planned or scripted. Every future episode
        will be planned and written strictly against what you produce here, so
        it must be internally consistent, unambiguous, and reusable across many
        future episodes without ever needing to be rewritten.

        Rules:
        - Never invent a main character or location beyond what was given - you
          may add supporting detail, but the cast/world you are given is
          authoritative.
        - When a character or location's appearance is explicitly given
          (e.g. an "(Appearance: ...)" note), you MUST reuse that exact
          appearance verbatim in characterDefinitions and in
          visualConsistencyRules - never invent, alter, or contradict a
          given appearance with a different one.
        - Every fact you state must not contradict any other fact you state.
        - Prefer concrete, specific detail over vague generalities - future
          agents will rely on this bible as ground truth.

        Always respond with ONLY a single JSON object, no markdown fences,
        matching exactly this schema:
        {"premise": string, "worldRules": [string], "characterDefinitions": string, "characterRelationships": string, "visualConsistencyRules": [string], "tone": string, "recurringElements": [string], "storyConstraints": [string], "storyArc": string or null}

        Field guidance:
        - premise: one tight paragraph synthesizing the series' core premise/promise.
        - worldRules: short standalone sentences stating how this story's world/setting works and its limits.
        - characterDefinitions: one paragraph describing every given main character's identity, personality and voice - consistent with what was given, no new main characters.
        - characterRelationships: one paragraph describing how the given characters relate to each other.
        - visualConsistencyRules: short rules a video generator must follow every episode so character appearance, location appearance, and recurring props/outfits stay visually consistent episode to episode.
        - tone: one or two sentences naming the dominant tone/mood.
        - recurringElements: recurring motifs, catchphrases, running gags, or objects that should reappear across episodes.
        - storyConstraints: hard rules the story must NEVER violate (things that must never happen or never change).
        - storyArc: OPTIONAL. Only include a multi-episode overarching arc description if the input implies the story should progress toward something across episodes; otherwise return JSON null (the story is open-ended/episodic).
        """;

    private readonly ILlmRouter _router;

    public StoryPlannerAgent(ILlmRouter router)
    {
        _router = router;
    }

    public Task<StoryPlannerAgentOutput> GenerateAsync(StoryPlannerAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Series title: {input.Title}
            Theme: {Or(input.Theme)}
            Genre: {Or(input.Genre)}
            Desired tone: {Or(input.Tone)}
            Target audience: {Or(input.TargetAudience)}
            Desired episode count: {(input.DesiredEpisodeCount is { } count ? count.ToString() : "(open-ended - no fixed count)")}

            Main characters (authoritative - do not invent others):
            {OrList(input.MainCharacters)}

            Locations / world elements (authoritative - do not invent others):
            {OrList(input.Locations)}

            Story rules / constraints the user wants enforced:
            {Or(input.StoryRulesConstraints)}

            Write the Story Bible now.
            """;

        return JsonAgentRunner.RunAsync<StoryPlannerAgentOutput>(
            _router.Resolve(LlmTaskType.Script),
            SystemPrompt,
            userPrompt,
            validate: output => !string.IsNullOrWhiteSpace(output.Premise) && !string.IsNullOrWhiteSpace(output.CharacterDefinitions),
            cancellationToken);
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(not specified)" : value.Trim();

    private static string OrList(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 ? "(none given)" : string.Join("\n", values.Select(v => $"- {v}"));
}
