using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Stories.Continuity;

/// <param name="StoryTitle">The parent Story's title.</param>
/// <param name="BibleSummary">Truncated flattened Story Bible.</param>
/// <param name="FinalizedScript">The episode's finalized script text (truncated) - the source of truth for what changed.</param>
/// <param name="CurrentLocation">The state's current fields BEFORE this episode, given for context.</param>
public record StoryContinuityAnalysisAgentInput(
    string StoryTitle,
    string BibleSummary,
    string FinalizedScript,
    string? CurrentLocation,
    string? CurrentObjective,
    string? CharacterStates,
    IReadOnlyList<string>? ImportantEvents,
    IReadOnlyList<string>? OpenStoryThreads,
    IReadOnlyList<string>? UnresolvedConflicts,
    IReadOnlyList<string>? KnownFacts,
    string? NextPlannedDestination,
    string? Notes);

/// <summary>
/// Full new canonical field values for <see cref="AiContentFactory.Domain.Stories.StoryState"/>
/// after this episode - NOT deltas. <see cref="AiContentFactory.Domain.Stories.StoryState.Update"/>
/// replaces every field wholesale, so the caller passes these values
/// straight through.
/// </summary>
public record StoryContinuityAnalysisOutput(
    string? CurrentLocation,
    string? CurrentObjective,
    string? CharacterStates,
    List<string>? ImportantEvents,
    List<string>? OpenStoryThreads,
    List<string>? UnresolvedConflicts,
    List<string>? KnownFacts,
    string? NextPlannedDestination,
    string? Notes);

public interface IStoryContinuityAnalysisAgent
{
    Task<StoryContinuityAnalysisOutput> GenerateAsync(StoryContinuityAnalysisAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// The LLM-backed half of "StoryContinuityManager": reads a just-finalized
/// episode's script against the Bible and the state as it stood before this
/// episode, and produces what the canonical <see cref="AiContentFactory.Domain.Stories.StoryState"/>
/// should become. Only called by <see cref="StoryContinuityManager.FinalizeEpisodeAsync"/>,
/// and only after <see cref="IContinuityValidatorAgent"/> has confirmed the
/// episode is valid.
/// </summary>
public class StoryContinuityAnalysisAgent : IStoryContinuityAnalysisAgent
{
    private const string SystemPrompt = """
        You are a continuity tracker for an ongoing multi-episode short-form
        video series. You are given a just-finished episode's script, the
        series' Story Bible, and the canonical continuity state as it stood
        BEFORE this episode. Determine what the canonical state should become
        AFTER this episode.

        Rules:
        - Return the FULL, final value for every field - not just what changed.
          For list fields (importantEvents, openStoryThreads, unresolvedConflicts,
          knownFacts): start from the given "before" list, then add anything new
          this episode established, and remove/resolve anything this episode
          explicitly resolved or closed. The result must be the complete,
          up-to-date list, not only the new entries.
        - Never invent a fact that is not supported by the script.
        - Never contradict the Story Bible.
        - If nothing changed for a field, return it unchanged.

        Always respond with ONLY a single JSON object, no markdown fences,
        matching exactly this schema:
        {"currentLocation": string, "currentObjective": string, "characterStates": string, "importantEvents": [string], "openStoryThreads": [string], "unresolvedConflicts": [string], "knownFacts": [string], "nextPlannedDestination": string, "notes": string}
        """;

    private readonly ILlmRouter _router;

    public StoryContinuityAnalysisAgent(ILlmRouter router)
    {
        _router = router;
    }

    public Task<StoryContinuityAnalysisOutput> GenerateAsync(StoryContinuityAnalysisAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Series: {input.StoryTitle}

            Story Bible:
            {input.BibleSummary}

            State BEFORE this episode:
            - Current location: {Or(input.CurrentLocation)}
            - Current objective: {Or(input.CurrentObjective)}
            - Character states: {Or(input.CharacterStates)}
            - Important events so far: {OrList(input.ImportantEvents)}
            - Open story threads: {OrList(input.OpenStoryThreads)}
            - Unresolved conflicts: {OrList(input.UnresolvedConflicts)}
            - Known facts: {OrList(input.KnownFacts)}
            - Next planned destination: {Or(input.NextPlannedDestination)}
            - Notes: {Or(input.Notes)}

            This episode's finalized script:
            {input.FinalizedScript}

            Produce the updated canonical state now.
            """;

        return JsonAgentRunner.RunAsync<StoryContinuityAnalysisOutput>(
            _router.Resolve(LlmTaskType.Script),
            SystemPrompt,
            userPrompt,
            validate: output => output.ImportantEvents is not null && output.OpenStoryThreads is not null,
            cancellationToken);
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(not specified)" : value.Trim();

    private static string OrList(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 ? "(none)" : string.Join("; ", values);
}
