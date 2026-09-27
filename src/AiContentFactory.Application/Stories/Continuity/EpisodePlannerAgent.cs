using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Stories.Continuity;

/// <param name="StoryTitle">The parent Story's title.</param>
/// <param name="EpisodeNumber">This episode's number - used to reinforce "continue from here, do not reset to episode zero".</param>
/// <param name="BibleSummary">Truncated flattened Story Bible (see <see cref="ContinuityPromptContext.BuildBibleSummary"/>).</param>
/// <param name="CurrentStoryArc">The Bible's overarching arc, if any.</param>
/// <param name="CurrentLocation">Current canonical <see cref="AiContentFactory.Domain.Stories.StoryState.CurrentLocation"/>.</param>
/// <param name="CurrentObjective">Current canonical <see cref="AiContentFactory.Domain.Stories.StoryState.CurrentObjective"/>.</param>
/// <param name="OpenStoryThreads">Current canonical open threads - the planner must address at least one where natural.</param>
/// <param name="PreviousEpisodeSummary">Truncated Summary of the single previous episode (resolved via PreviousEpisodeId, one hop only) - never a full script, never multiple episodes.</param>
/// <param name="PreviousEpisodeEnding">Short "how it ended" cue derived from the previous episode's Summary (see <see cref="ContinuityPromptContext.DerivePreviousEnding"/>).</param>
public record EpisodePlannerAgentInput(
    string StoryTitle,
    int EpisodeNumber,
    string BibleSummary,
    string? CurrentStoryArc,
    string? CurrentLocation,
    string? CurrentObjective,
    IReadOnlyList<string>? OpenStoryThreads,
    string? PreviousEpisodeSummary,
    string? PreviousEpisodeEnding);

public record EpisodePlannerAgentOutput(
    string? Title,
    string? Objective,
    string? Setup,
    List<string>? MajorBeats,
    string? Conflict,
    string? Escalation,
    string? Resolution,
    string? Cliffhanger,
    List<string>? ContinuityRequirements,
    List<string>? ScenesRequired);

public interface IEpisodePlannerAgent
{
    Task<EpisodePlannerAgentOutput> GenerateAsync(EpisodePlannerAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Plans ONE episode's production outline from the Story Bible, the current
/// canonical Story State, and the single previous episode's summary/ending -
/// never from any earlier episode's full script (see the IMPORTANT CONTEXT
/// RULE on <see cref="ContinuityPromptContext"/>).
/// </summary>
public class EpisodePlannerAgent : IEpisodePlannerAgent
{
    private const string SystemPrompt = """
        You are an episode planner for an ongoing multi-episode short-form
        vertical video series. You are given the series' Story Bible, the
        CURRENT canonical continuity state, and a summary of the single
        immediately-preceding episode. Plan ONLY the next episode's outline.

        CRITICAL continuity rules - never violate these:
        1. CONTINUE from the given current state - do not restart, reboot, or
           reset the story to episode zero. This is episode number given below,
           not episode one, unless the state and previous-episode context show
           this genuinely is the first episode.
        2. Never contradict any fact in the Story Bible or the current state.
        3. Address AT LEAST ONE of the given open story threads where it is
           natural to do so - do not silently ignore all of them every episode.
        4. Stay consistent with the Bible's overarching Story Arc when one is
           given - this episode should be a meaningful step along it, not a
           disconnected detour.

        Always respond with ONLY a single JSON object, no markdown fences,
        matching exactly this schema:
        {"title": string, "objective": string, "setup": string, "majorBeats": [string], "conflict": string, "escalation": string, "resolution": string, "cliffhanger": string, "continuityRequirements": [string], "scenesRequired": [string]}

        Field guidance:
        - title: a short working title for this episode.
        - objective: what this episode needs to accomplish narratively.
        - setup: where/how the episode opens, consistent with the current state.
        - majorBeats: the ordered list of major plot beats for this episode.
        - conflict / escalation / resolution / cliffhanger: the episode's dramatic shape.
        - continuityRequirements: specific facts/threads from the given state and Bible that the eventual script MUST honour and must NOT contradict.
        - scenesRequired: short descriptions of the discrete scenes needed to cover this outline, in order.
        """;

    private readonly ILlmRouter _router;

    public EpisodePlannerAgent(ILlmRouter router)
    {
        _router = router;
    }

    public Task<EpisodePlannerAgentOutput> GenerateAsync(EpisodePlannerAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Series: {input.StoryTitle}
            Episode number to plan: {input.EpisodeNumber} (continue from here - do not reset to episode zero)

            Story Bible:
            {input.BibleSummary}

            Overarching story arc (if any): {Or(input.CurrentStoryArc)}

            Current canonical state:
            - Current location: {Or(input.CurrentLocation)}
            - Current objective: {Or(input.CurrentObjective)}
            - Open story threads: {OrList(input.OpenStoryThreads)}

            Previous episode summary: {Or(input.PreviousEpisodeSummary)}
            How the previous episode ended: {Or(input.PreviousEpisodeEnding)}

            Plan episode {input.EpisodeNumber} now.
            """;

        return JsonAgentRunner.RunAsync<EpisodePlannerAgentOutput>(
            _router.Resolve(LlmTaskType.Script),
            SystemPrompt,
            userPrompt,
            validate: output => !string.IsNullOrWhiteSpace(output.Objective) && output.MajorBeats is { Count: > 0 },
            cancellationToken);
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(not specified)" : value.Trim();

    private static string OrList(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 ? "(none)" : string.Join("; ", values);
}
