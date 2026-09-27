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
/// <param name="StoryVisualContext">
/// Optional - only populated when the owning ContentProject is linked to a
/// Story episode. A short, already-truncated composed string (Story Bible
/// visual consistency rules + cast/location visual descriptions, plus the
/// linked episode's own frozen current location/objective/recent events
/// when it has been completed) so a scene's shot data doesn't contradict the
/// Story's established look or current state. Null for every non-Story
/// project (the default/normal case), which leaves the generated prompt
/// byte-for-byte identical to before this field existed.
/// </param>
/// <param name="ReferenceNames">
/// Names of the recurring characters/locations that have (or will have) a
/// reference image - e.g. the Story cast and locations. The agent reports which
/// of them are actually visible in THIS shot. Null/empty = none known.
/// </param>
/// <param name="PreviousShot">
/// The previous scene's framing + camera (e.g. "Medium, SlowPushIn") so the
/// agent can vary consecutive shots instead of repeating one. Null = first
/// scene or previous scene not prompted yet.
/// </param>
public record PromptAgentInput(
    string Narration,
    string VisualDescription,
    string CameraDirection,
    SceneVisualType VisualType,
    string? StyleGuidance,
    string? StyleNegativePrompt = null,
    string? StoryVisualContext = null,
    IReadOnlyList<string>? ReferenceNames = null,
    string? PreviousShot = null);

/// <param name="Action">
/// One specific, observable primary action for a ~8s clip. This is structured
/// scene data - the deterministic <see cref="Generation.VideoPromptBuilder"/>
/// turns it (plus camera/style/references) into the final prompt; the agent no
/// longer writes the whole prompt itself.
/// </param>
/// <param name="Camera">One camera behaviour chosen from the fixed <see cref="CameraMovement"/> set.</param>
/// <param name="Shot">The framing chosen from the fixed <see cref="ShotSize"/> set; Unspecified when the model gave none/an unknown value.</param>
/// <param name="VisibleReferenceNames">
/// The subset of <see cref="PromptAgentInput.ReferenceNames"/> visible in this
/// shot, canonical casing, never a name outside that list. Null when no names
/// were offered (nothing to decide).
/// </param>
/// <param name="CharacterOnScreen">Whether a recurring character is visible in this shot; null when the model gave no usable answer.</param>
public record PromptAgentOutput(
    string Action,
    CameraMovement Camera,
    string? NegativePrompt,
    string? VisualStyle,
    ShotSize Shot = ShotSize.Unspecified,
    IReadOnlyList<string>? VisibleReferenceNames = null,
    bool? CharacterOnScreen = null);

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
        - name the subject with its key visible traits, WHERE it happens (a concrete place and
          time of day) and the LIGHT on it (e.g. "in a dim stone temple, lit by slanting
          late-afternoon sunbeams") - never leave the setting or light implied
        - describe animals and objects as what they are; never give an animal human anatomy,
          clothing or an upright human pose unless the series context explicitly says so
        - never vague ("performs an action", "does something", "reacts to the scene")
        - plain English, ONE sentence of at most 45 words, no headings, no labels, no line breaks
        - do not mention camera, framing, lens or art style - those are separate fields
        - for a still image, describe the single frozen moment instead of a movement

        Rules for "shot": the framing, choose exactly one of
        ExtremeWide, Wide, Medium, MediumCloseUp, CloseUp, ExtremeCloseUp.
        Pick what best shows the action (wide for places/crowds, close-up for faces/details).

        Rules for "camera": choose exactly one of
        Static, SlowPushIn, SlowPullOut, HandheldFollow, SideTracking, ForwardTracking, OverShoulder.
        If a previous shot is given, choose a DIFFERENT shot/camera combination unless the story
        clearly needs the same one - consecutive identical shots look monotonous.

        Rules for "visibleReferences": from the given list of recurring names ONLY, the names whose
        character or location is actually VISIBLE in this shot - judged from what the shot shows,
        not from names merely mentioned in the narration. Empty array when none is visible.

        Rules for "characterOnScreen": true only when a recurring character (one of the named
        characters, or the series' main character) is visible in this shot; false for shots of
        places, objects, crowds, other animals or other people.

        "negativePrompt": only when it meaningfully helps (exclude text/watermarks/deformities), else null.
        "visualStyle": usually null - the project already has a style; only set it if the scene truly needs a deviation.

        Respond with ONLY a single JSON object, no markdown fences, matching exactly:
        {"action": string, "shot": "ExtremeWide"|"Wide"|"Medium"|"MediumCloseUp"|"CloseUp"|"ExtremeCloseUp", "camera": "Static"|"SlowPushIn"|"SlowPullOut"|"HandheldFollow"|"SideTracking"|"ForwardTracking"|"OverShoulder", "visibleReferences": string[], "characterOnScreen": boolean, "negativePrompt": string|null, "visualStyle": string|null}
        """;

    private readonly ILlmProvider _llmProvider;

    public PromptAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    private record RawOutput(
        string? Action,
        string? Camera,
        string? NegativePrompt,
        string? VisualStyle,
        string? Shot = null,
        List<string>? VisibleReferences = null,
        bool? CharacterOnScreen = null);

    public async Task<PromptAgentOutput> GenerateAsync(PromptAgentInput input, CancellationToken cancellationToken = default)
    {
        var typeGuidance = input.VisualType == SceneVisualType.AiImage
            ? "Asset type: a SINGLE STILL IMAGE (no motion). Describe the one frozen moment; still choose the closest camera framing."
            : "Asset type: a short VIDEO CLIP (~8s). Describe one continuous, visible action.";

        var storyContextLine = string.IsNullOrWhiteSpace(input.StoryVisualContext)
            ? string.Empty
            : $"\nSeries continuity requirements (must not contradict): {input.StoryVisualContext}";

        var referenceNames = (input.ReferenceNames ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var userPrompt = $"""
            Scene narration (context only, never shown or spoken by the visual): {input.Narration}
            Existing visual description (may be empty): {Blankable(input.VisualDescription)}
            Existing camera note (may be empty): {Blankable(input.CameraDirection)}
            {typeGuidance}
            Project visual style (do not restate it in the action): {input.StyleGuidance ?? "(none specified)"}
            Style exclusions to respect: {input.StyleNegativePrompt ?? "(none specified)"}{storyContextLine}
            Recurring names (for visibleReferences): {(referenceNames.Count == 0 ? "(none)" : string.Join(", ", referenceNames))}
            Previous shot (shot, camera): {Blankable(input.PreviousShot)}
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
        var shot = Enum.TryParse<ShotSize>(raw.Shot?.Trim(), ignoreCase: true, out var parsedShot) && Enum.IsDefined(parsedShot)
            ? parsedShot
            : ShotSize.Unspecified;

        return new PromptAgentOutput(
            Action: raw.Action!.Trim(),
            Camera: camera,
            NegativePrompt: MergeNegativePrompts(raw.NegativePrompt, input.StyleNegativePrompt),
            VisualStyle: string.IsNullOrWhiteSpace(raw.VisualStyle) ? null : raw.VisualStyle.Trim(),
            Shot: shot,
            VisibleReferenceNames: referenceNames.Count == 0 ? null : KnownNamesOnly(raw.VisibleReferences, referenceNames),
            CharacterOnScreen: raw.CharacterOnScreen);
    }

    /// <summary>
    /// The model's visible-name list restricted to names it was actually
    /// offered (canonical casing) - an invented or misspelled name must never
    /// become a reference tag.
    /// </summary>
    public static IReadOnlyList<string> KnownNamesOnly(IEnumerable<string>? returned, IReadOnlyList<string> offered) =>
        (returned ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => offered.FirstOrDefault(o => string.Equals(o, n.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
