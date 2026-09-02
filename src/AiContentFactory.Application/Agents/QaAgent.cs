using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Agents;

public record QaAgentInput(string Title, ScriptAgentOutput Script);

public record QaAgentOutput(
    double Hook,
    double Story,
    double Pacing,
    double FactualAccuracy,
    double PlatformSuitability,
    double Overall,
    string Notes);

public interface IQaAgent
{
    Task<QaAgentOutput> ScoreAsync(QaAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs as a cheap pre-flight gate right after script generation, BEFORE any
/// Veo spend happens - scoring a finished render can't undo already-spent
/// generation cost, so this only looks at the script text (no storyboard/
/// visuals exist yet at this point in the pipeline).
/// </summary>
public class QaAgent : IQaAgent
{
    private const string SystemPrompt = """
        You are a strict short-form video script reviewer for TikTok/Reels/Shorts.
        You are reviewing ONLY the script, before any video has been produced -
        your job is to catch a weak script before money is spent generating
        video from it. Score each dimension 0-10 (10 = excellent). Be
        genuinely critical - most first drafts should NOT score above 8 on
        Hook or Story unless truly exceptional.
        Always respond with ONLY a single JSON object, no markdown fences,
        matching exactly this schema:
        {"hook": number, "story": number, "pacing": number, "factualAccuracy": number, "platformSuitability": number, "overall": number, "notes": string}
        """;

    private readonly ILlmProvider _llmProvider;

    public QaAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public Task<QaAgentOutput> ScoreAsync(QaAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Title: {input.Title}

            Script:
            Hook: {input.Script.Hook}
            Introduction: {input.Script.Introduction}
            Body: {input.Script.Body}
            Escalation: {input.Script.Escalation}
            Payoff: {input.Script.Payoff}
            Call to action: {input.Script.CallToAction}
            """;

        return JsonAgentRunner.RunAsync<QaAgentOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: output => output.Overall is >= 0 and <= 10,
            cancellationToken);
    }
}
