using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Agents;

public record ScriptAgentInput(string Title, string? Topic, string? Niche, int TargetDurationSeconds);

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

            Write a script sized to be spoken comfortably within the target duration
            (roughly 2.5 words/second as a guide). Make the hook punchy and specific,
            not generic. Avoid a throat-clearing introduction - get to the point.
            """;

        return JsonAgentRunner.RunAsync<ScriptAgentOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: output => !string.IsNullOrWhiteSpace(output.Hook) && !string.IsNullOrWhiteSpace(output.Body),
            cancellationToken);
    }
}
