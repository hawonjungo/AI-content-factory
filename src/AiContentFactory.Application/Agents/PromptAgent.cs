using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Agents;

/// <param name="StyleGuidance">
/// Visual direction from the project's style preset (see PresetCatalog). This
/// used to be a hardcoded "dark fantasy, cinematic, moody lighting" fallback,
/// which meant every project looked the same regardless of subject.
/// </param>
/// <param name="StyleNegativePrompt">
/// The style preset's exclusions, merged into whatever the model decides to
/// exclude on its own.
/// </param>
public record PromptAgentInput(
    string Narration,
    string VisualDescription,
    string CameraDirection,
    SceneVisualType VisualType,
    string? StyleGuidance,
    string? StyleNegativePrompt = null);

/// <param name="Action">
/// One specific, observable primary action for a ~8s clip. This is structured
/// scene data - the deterministic <see cref="Generation.VideoPromptBuilder"/>
/// turns it (plus camera/style/references) into the final prompt; the agent no
/// longer writes the whole prompt itself.
/// </param>
/// <param name="Camera">One camera behaviour chosen from the fixed <see cref="CameraMovement"/> set.</param>
public record PromptAgentOutput(string Action, CameraMovement Camera, string? NegativePrompt, string? VisualStyle);

public interface IPromptAgent
{
    Task<PromptAgentOutput> GenerateAsync(PromptAgentInput input, CancellationToken cancellationToken = default);
}

public class PromptAgent : IPromptAgent
{
    private const string SystemPrompt = """
        You extract STRUCTURED shot data from one storyboard scene for an AI video/image model.
        You do NOT write the final prompt - application code assembles that deterministically.

        Rules for "action":
        - ONE single, specific, physically-realistic primary action that fits in about 8 seconds
        - observable on screen (a movement, not a feeling and not the narration text)
        - one subject, one continuous action; if the scene implies several actions, pick the
          single most important one and describe only that (no "then", no lists, no sequence)
        - never vague ("performs an action", "does something", "reacts to the scene")
        - plain English, ONE sentence, no headings, no labels, no line breaks
        - for a still image, describe the single frozen moment instead of a movement

        Rules for "camera": choose exactly one of
        Static, SlowPushIn, SlowPullOut, HandheldFollow, SideTracking, ForwardTracking, OverShoulder.

        "negativePrompt": only when it meaningfully helps (exclude text/watermarks/deformities), else null.
        "visualStyle": usually null - the project already has a style; only set it if the scene truly needs a deviation.

        Respond with ONLY a single JSON object, no markdown fences, matching exactly:
        {"action": string, "camera": "Static"|"SlowPushIn"|"SlowPullOut"|"HandheldFollow"|"SideTracking"|"ForwardTracking"|"OverShoulder", "negativePrompt": string|null, "visualStyle": string|null}
        """;

    private readonly ILlmProvider _llmProvider;

    public PromptAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    private record RawOutput(string? Action, string? Camera, string? NegativePrompt, string? VisualStyle);

    public async Task<PromptAgentOutput> GenerateAsync(PromptAgentInput input, CancellationToken cancellationToken = default)
    {
        var typeGuidance = input.VisualType == SceneVisualType.AiImage
            ? "Asset type: a SINGLE STILL IMAGE (no motion). Describe the one frozen moment; still choose the closest camera framing."
            : "Asset type: a short VIDEO CLIP (~8s). Describe one continuous, visible action.";

        var userPrompt = $"""
            Scene narration (context only, never shown or spoken by the visual): {input.Narration}
            Existing visual description (may be empty): {Blankable(input.VisualDescription)}
            Existing camera note (may be empty): {Blankable(input.CameraDirection)}
            {typeGuidance}
            Project visual style (do not restate it in the action): {input.StyleGuidance ?? "(none specified)"}
            Style exclusions to respect: {input.StyleNegativePrompt ?? "(none specified)"}
            """;

        var raw = await JsonAgentRunner.RunAsync<RawOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: result => !string.IsNullOrWhiteSpace(result.Action),
            cancellationToken);

        var camera = Enum.TryParse<CameraMovement>(raw.Camera?.Trim(), ignoreCase: true, out var parsed)
            ? parsed
            : CameraMovement.Unspecified;

        // The preset's exclusions are a hard requirement, so they are merged in
        // here rather than left to the model's discretion - it routinely
        // returns a null negative prompt even when told about exclusions.
        return new PromptAgentOutput(
            Action: raw.Action!.Trim(),
            Camera: camera,
            NegativePrompt: MergeNegativePrompts(raw.NegativePrompt, input.StyleNegativePrompt),
            VisualStyle: string.IsNullOrWhiteSpace(raw.VisualStyle) ? null : raw.VisualStyle.Trim());
    }

    private static string Blankable(string? value) => string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();

    private static string? MergeNegativePrompts(string? modelNegative, string? styleNegative)
    {
        var parts = new[] { modelNegative, styleNegative }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());

        var merged = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(merged) ? null : merged;
    }
}
