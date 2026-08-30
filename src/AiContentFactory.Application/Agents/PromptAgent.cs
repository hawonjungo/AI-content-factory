using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Agents;

public record PromptAgentInput(
    string Narration,
    string VisualDescription,
    string CameraDirection,
    SceneVisualType VisualType,
    string? StyleGuidance);

public record PromptAgentOutput(string Prompt, string? NegativePrompt, string? VisualStyle);

public interface IPromptAgent
{
    Task<PromptAgentOutput> GenerateAsync(PromptAgentInput input, CancellationToken cancellationToken = default);
}

public class PromptAgent : IPromptAgent
{
    private const string SystemPrompt = """
        You write generation prompts for AI video/image models from a storyboard
        scene description. Include camera direction, lighting, environment, and
        mood explicitly. Keep visual consistency in mind (style should read as
        part of one coherent piece, not a random assortment). Provide a negative
        prompt only when it meaningfully helps (e.g. excluding text/watermarks/
        deformities) - otherwise return null.
        Always respond with ONLY a single JSON object, no markdown fences, matching
        exactly this schema:
        {"prompt": string, "negativePrompt": string|null, "visualStyle": string|null}
        """;

    private readonly ILlmProvider _llmProvider;

    public PromptAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    public Task<PromptAgentOutput> GenerateAsync(PromptAgentInput input, CancellationToken cancellationToken = default)
    {
        var userPrompt = $"""
            Scene narration (for context/timing, not to be shown on screen): {input.Narration}
            Visual description: {input.VisualDescription}
            Camera direction: {input.CameraDirection}
            Asset type: {input.VisualType}
            Style guidance: {input.StyleGuidance ?? "dark fantasy, cinematic, moody lighting"}
            """;

        return JsonAgentRunner.RunAsync<PromptAgentOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: output => !string.IsNullOrWhiteSpace(output.Prompt),
            cancellationToken);
    }
}
