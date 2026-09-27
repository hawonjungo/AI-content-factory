using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Stories.Continuity;

/// <param name="StoryTitle">The parent Story's title.</param>
/// <param name="BibleSummary">Truncated flattened Story Bible (see <see cref="ContinuityPromptContext.BuildBibleSummary"/>).</param>
/// <param name="VisualConsistencyRules">The Bible's visual consistency rules, joined - reference explicitly so shot descriptions stay consistent with prior episodes.</param>
/// <param name="CharacterDefinitions">The Bible's character definitions paragraph - preserve identity/voice.</param>
/// <param name="CurrentLocation">Current canonical <see cref="AiContentFactory.Domain.Stories.StoryState.CurrentLocation"/>.</param>
/// <param name="CurrentObjective">Current canonical <see cref="AiContentFactory.Domain.Stories.StoryState.CurrentObjective"/>.</param>
/// <param name="CharacterStates">Current canonical <see cref="AiContentFactory.Domain.Stories.StoryState.CharacterStates"/> free-form notes.</param>
/// <param name="PreviousEpisodeSummary">Truncated Summary of the single previous episode only.</param>
/// <param name="OutlineSummary">This episode's own persisted Outline, flattened (see <see cref="ContinuityPromptContext.BuildOutlineSummary"/>) - the production plan to write to.</param>
/// <param name="Language">Narration language.</param>
public record StoryScriptWriterAgentInput(
    string StoryTitle,
    string BibleSummary,
    string VisualConsistencyRules,
    string? CharacterDefinitions,
    string? CurrentLocation,
    string? CurrentObjective,
    string? CharacterStates,
    string? PreviousEpisodeSummary,
    string OutlineSummary,
    string? Language);

/// <summary>
/// Field-compatible with <see cref="AiContentFactory.Application.Agents.ScriptAgentOutput"/>
/// (Hook/Introduction/Body/Escalation/Payoff/CallToAction) so a future
/// integration step can feed a Story episode's script into the same
/// storyboard/scene pipeline without inventing a parallel shape. Adds an
/// optional Summary the writer may produce directly (used as the episode's
/// <see cref="AiContentFactory.Domain.Stories.StoryEpisode.Summary"/> when
/// given, instead of a mechanical fallback).
/// </summary>
public record StoryScriptWriterOutput(
    string Hook,
    string Introduction,
    string Body,
    string Escalation,
    string Payoff,
    string CallToAction,
    string? Summary);

public interface IStoryScriptWriterAgent
{
    Task<StoryScriptWriterOutput> GenerateAsync(StoryScriptWriterAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes ONE episode's production-ready script from its own persisted
/// Outline, the Story Bible, and the current canonical Story State - never
/// from any earlier episode's full script (see the IMPORTANT CONTEXT RULE on
/// <see cref="ContinuityPromptContext"/>). Named distinctly from the
/// existing single-shot <c>ScriptAgent</c> (which writes ContentProject
/// scripts, unrelated to the Story/Series module) to avoid any confusion
/// between the two.
/// </summary>
public class StoryScriptWriterAgent : IStoryScriptWriterAgent
{
    private const string SystemPrompt = """
        You are a script writer for an ongoing multi-episode short-form
        vertical video series (TikTok/Reels/Shorts). You write ONE episode's
        narration script from its own approved outline, the series' Story
        Bible, and the current canonical continuity state.

        CRITICAL continuity rules - never violate these:
        1. Preserve every character's identity and voice exactly as described
           in the Bible's character definitions - do not change personality,
           speech style, or established traits.
        2. Preserve location continuity - characters must be where the current
           state says they are, unless the outline's setup explicitly moves
           them, and any move must be explained, never a silent teleport.
        3. Preserve narrative continuity - do not contradict the current
           objective, character states, or any fact implied by the previous
           episode summary.
        4. Preserve visual consistency - honour the Bible's visual consistency
           rules given below in any physical/visual descriptions.
        5. Do not silently drop the outline's continuity requirements - the
           script must address every one of them; if one genuinely cannot fit,
           it must still not be contradicted.
        6. Follow the outline's beats/conflict/escalation/resolution/
           cliffhanger shape - do not invent a different plot.

        Write for a single short-form vertical video: punchy, no wasted words,
        a hook in the first line, natural pacing toward the outline's
        resolution/cliffhanger.

        Always respond with ONLY a single JSON object, no markdown fences,
        matching exactly this schema:
        {"hook": string, "introduction": string, "body": string, "escalation": string, "payoff": string, "callToAction": string, "summary": string}

        - hook/introduction/body/escalation/payoff/callToAction: the narration script sections, in order.
        - summary: 2-3 sentences recapping what happened in THIS episode (for future continuity context) - do not include next-episode teasers here, only what actually happened.
        """;

    private readonly ILlmRouter _router;

    public StoryScriptWriterAgent(ILlmRouter router)
    {
        _router = router;
    }

    public Task<StoryScriptWriterOutput> GenerateAsync(StoryScriptWriterAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Series: {input.StoryTitle}
            Write the narration in: {LanguageName(input.Language)}.

            Story Bible:
            {input.BibleSummary}

            Visual consistency rules (honour in any visual/physical descriptions): {input.VisualConsistencyRules}
            Character definitions (preserve identity/voice exactly): {Or(input.CharacterDefinitions)}

            Current canonical state:
            - Current location: {Or(input.CurrentLocation)}
            - Current objective: {Or(input.CurrentObjective)}
            - Character states: {Or(input.CharacterStates)}

            Previous episode summary: {Or(input.PreviousEpisodeSummary)}

            This episode's approved outline (write to this plan):
            {input.OutlineSummary}

            Write the episode script now.
            """;

        return JsonAgentRunner.RunAsync<StoryScriptWriterOutput>(
            _router.Resolve(LlmTaskType.Script),
            SystemPrompt,
            userPrompt,
            validate: output => !string.IsNullOrWhiteSpace(output.Hook) && !string.IsNullOrWhiteSpace(output.Body),
            cancellationToken);
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(not specified)" : value.Trim();

    /// <summary>Same language-code-to-name mapping approach as <see cref="AiContentFactory.Application.Agents.ScriptAgent"/> - spelling the language out is noticeably more reliable than passing the bare code.</summary>
    private static string LanguageName(string? code) => (code ?? "en").Trim().ToLowerInvariant() switch
    {
        "vi" or "vi-vn" => "Vietnamese",
        "en" or "en-us" or "en-gb" => "English",
        "ja" => "Japanese",
        "ko" => "Korean",
        "zh" => "Chinese",
        "th" => "Thai",
        "id" => "Indonesian",
        "es" => "Spanish",
        "fr" => "French",
        var other => other
    };
}
