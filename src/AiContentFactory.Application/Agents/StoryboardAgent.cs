using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Agents;

public record StoryboardAgentInput(string Title, ScriptAgentOutput Script, int TargetDurationSeconds);

public record StoryboardAgentScene(
    int SceneNumber,
    int DurationSeconds,
    string Narration,
    string VisualDescription,
    string CameraDirection,
    SceneVisualType VisualType);

public record StoryboardAgentOutput(IReadOnlyList<StoryboardAgentScene> Scenes);

public interface IStoryboardAgent
{
    Task<StoryboardAgentOutput> GenerateAsync(StoryboardAgentInput input, CancellationToken cancellationToken = default);
}

public class StoryboardAgent : IStoryboardAgent
{
    private const string SystemPrompt = """
        You convert a short-form video script into a scene-by-scene storyboard.
        Break the script into 4-8 scenes covering hook through call-to-action, in
        order, with scene durations summing to roughly the target duration.
        For visualType, choose the CHEAPEST option that still serves the scene:
        aiVideo only for hook/hero/high-impact moments; aiImage for simpler shots;
        motionGraphic/textAnimation/diagram for exposition, stats, or supporting
        points; existingFootage for generic B-roll. Do not make every scene aiVideo.
        Always respond with ONLY a single JSON object, no markdown fences, matching
        exactly this schema:
        {"scenes": [{"sceneNumber": number, "durationSeconds": number, "narration": string, "visualDescription": string, "cameraDirection": string, "visualType": "aiVideo"|"aiImage"|"existingFootage"|"motionGraphic"|"textAnimation"|"diagram"}]}
        """;

    private readonly ILlmProvider _llmProvider;

    public StoryboardAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public Task<StoryboardAgentOutput> GenerateAsync(StoryboardAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Title: {input.Title}
            Target total duration: {input.TargetDurationSeconds} seconds.

            Script:
            Hook: {input.Script.Hook}
            Introduction: {input.Script.Introduction}
            Body: {input.Script.Body}
            Escalation: {input.Script.Escalation}
            Payoff: {input.Script.Payoff}
            Call to action: {input.Script.CallToAction}
            """;

        return JsonAgentRunner.RunAsync<StoryboardAgentOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: output => output.Scenes.Count > 0 && output.Scenes.All(s => !string.IsNullOrWhiteSpace(s.Narration)),
            cancellationToken);
    }
}
