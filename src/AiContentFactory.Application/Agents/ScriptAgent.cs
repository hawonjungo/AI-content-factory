using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Agents;

/// <param name="TemplateGuidance">
/// Format instructions from the chosen content template (see PresetCatalog) -
/// this is what makes "Top 5" produce a countdown and "Kinh dị" produce a
/// slow burn, instead of every template producing the same generic script.
/// </param>
/// <param name="ContentPillar">Recurring content theme the video belongs to (e.g. "founder lessons").</param>
/// <param name="TargetAudience">Who it's for ("first-time founders", "cat owners").</param>
/// <param name="StoryType">Narrative shape the user asked for ("case study", "myth-busting", "listicle").</param>
/// <param name="HookStyle">How the opening should grab ("shocking stat", "bold claim", "question").</param>
/// <param name="Emotion">Dominant feeling to aim for ("awe", "tension", "warmth").</param>
public record ScriptAgentInput(
    string Title,
    string? Topic,
    string? Niche,
    int TargetDurationSeconds,
    string? TemplateGuidance = null,
    string? Language = null,
    string? ContentPillar = null,
    string? TargetAudience = null,
    string? StoryType = null,
    string? HookStyle = null,
    string? Emotion = null);

public record ScriptAgentOutput(
    string Hook,
    string Introduction,
    string Body,
    string Escalation,
    string Payoff,
    string CallToAction);

public interface IScriptAgent
{
    Task<ScriptAgentOutput> GenerateAsync(ScriptAgentInput input, CancellationToken cancellationToken = default);
}

public class ScriptAgent : IScriptAgent
{
    private const string SystemPrompt = """
        You are a short-form video scriptwriter for vertical (9:16) faceless content
        on TikTok/Reels/Shorts. You optimize aggressively for retention: no wasted
        words, a hook in the first line, and a natural payoff + call to action.
        Always respond with ONLY a single JSON object, no markdown fences, matching
        exactly this schema:
        {"hook": string, "introduction": string, "body": string, "escalation": string, "payoff": string, "callToAction": string}
        """;

    private readonly ILlmProvider _llmProvider;

    public ScriptAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public Task<ScriptAgentOutput> GenerateAsync(ScriptAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Title: {input.Title}
            Topic: {input.Topic ?? "(not specified - infer from title)"}
            Niche: {input.Niche ?? "(not specified - infer from title)"}
            Target total duration: {input.TargetDurationSeconds} seconds.
            Write the narration in: {LanguageName(input.Language)}.

            Write a script sized to be spoken comfortably within the target duration
            (roughly 2.5 words/second as a guide). Make the hook punchy and specific,
            not generic. Avoid a throat-clearing introduction - get to the point.

            Format requirements for this specific content template:
            {input.TemplateGuidance ?? "(no template selected - use a standard hook/story/payoff structure)"}

            Creative direction (honour whichever are given, ignore the blanks):
            - Content pillar: {Or(input.ContentPillar)}
            - Target audience: {Or(input.TargetAudience)}
            - Story type: {Or(input.StoryType)}
            - Hook style: {Or(input.HookStyle)}
            - Dominant emotion: {Or(input.Emotion)}
            """;

        return JsonAgentRunner.RunAsync<ScriptAgentOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: output => !string.IsNullOrWhiteSpace(output.Hook) && !string.IsNullOrWhiteSpace(output.Body),
            cancellationToken);
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "(not specified)" : value.Trim();

    /// <summary>
    /// The project stores an ISO-ish code ("vi", "en"); spelling the language
    /// out gives noticeably more reliable results than passing the bare code.
    /// Unknown codes are passed through - Gemini handles most of them.
    /// </summary>
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
