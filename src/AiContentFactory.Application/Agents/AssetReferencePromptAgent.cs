using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.AssetReferences;

namespace AiContentFactory.Application.Agents;

/// <param name="AssetType">"Character" or "Environment" - changes what the prompt emphasises (see system prompt).</param>
/// <param name="StoryContext">The project's actual script (hook/introduction/body/escalation/payoff), authoritative for subject/action/setting/tone.</param>
/// <param name="StoryType">Step 2 "Kiểu chuyện" hint (may be null).</param>
/// <param name="HookStyle">Step 2 "Kiểu hook" hint (may be null).</param>
/// <param name="Emotion">Step 2 "Cảm xúc chủ đạo" hint (may be null).</param>
/// <param name="StyleGuidance">The project's style preset guidance - mandatory, must never be contradicted.</param>
/// <param name="StyleNegativePrompt">The style preset's own exclusions, merged into the agent's derived negative prompt.</param>
public record AssetReferencePromptAgentInput(
    string AssetType,
    string Title,
    string? Topic,
    string? Niche,
    string StoryContext,
    string? StoryType,
    string? HookStyle,
    string? Emotion,
    string StyleGuidance,
    string? StyleNegativePrompt,
    string AspectRatio);

/// <summary>The intermediate "visual analysis" the agent reasons through before writing the final prompt - kept in the response for transparency/debugging, not just discarded.</summary>
public record AssetReferenceVisualAnalysis(
    IReadOnlyList<string> MainSubjects,
    string Action,
    string Environment,
    string Emotion,
    string Tone,
    string VisualStyle,
    string Lighting,
    string ColorPalette,
    string Composition);

public record AssetReferencePromptOutput(AssetReferenceVisualAnalysis Analysis, string Prompt, string NegativePrompt);

public interface IAssetReferencePromptAgent
{
    Task<AssetReferencePromptOutput> GenerateAsync(AssetReferencePromptAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns a project's actual script into an accurate reference-image prompt,
/// replacing the old approach where the Character prompt was a static
/// template that hardcoded "a human" regardless of what the story was about.
/// Subject, art style and mood are all derived from the script (plus the
/// project's style preset and Step 2 story hints) - never assumed from the
/// niche and never hardcoded to a fixed species or style in code.
/// </summary>
public class AssetReferencePromptAgent : IAssetReferencePromptAgent
{
    /// <summary>Universal quality/anatomy safety net - never subject- or style-specific, so it never fights the model's own subject/style choice.</summary>
    public const string QualitySafetyNegative =
        "deformed, distorted, disfigured, mutated, extra limbs, extra fingers, fused fingers, bad hands, " +
        "bad anatomy, asymmetrical face, blurry, lowres, text, captions, subtitles, logos, watermark";

    /// <summary>
    /// For the "user typed/edited their own prompt" path, which never goes
    /// through this agent (their wording is sent verbatim) but still gets the
    /// same subject-agnostic quality safety net merged with the style's own
    /// exclusions - nothing about species or art style is added here.
    /// </summary>
    public static string MergeWithQualityNegative(string? styleNegative) => MergeNegative(null, styleNegative, null);

    /// <summary>
    /// Exclusions that are ALWAYS applied to a Character reference (an identity
    /// sheet against a plain white backdrop): anything that puts the character
    /// into a scene, plus the pose/composition failures of a reference sheet
    /// (cropped body, seated/crouching/leaning/dynamic pose, extreme
    /// perspective, side view). Deterministic and code-owned so it never depends
    /// on the LLM remembering to exclude them. Never species- or art-style-specific, and
    /// deliberately never "other characters"/"props": a project-level Character
    /// reference may legitimately have several main subjects (e.g. "boy and
    /// kitten") and a held item, which such terms could suppress.
    /// </summary>
    public const string CharacterSheetNegative =
        "background scenery, environment, landscape, room interior, furniture, text, watermark, " +
        "cropped, cut off, sitting, crouching, crossed arms, leaning, dynamic pose, extreme perspective, side view, " +
        "gradient background, patterned background, food";

    /// <summary>
    /// Extra exclusions for a SINGLE-subject character sheet only (the
    /// Story-level character reference, where exactly one character is
    /// guaranteed). Not applied on the agent / custom-prompt paths.
    /// </summary>
    public const string SingleCharacterSheetNegative = "props, other characters";

    /// <summary>
    /// <see cref="SingleCharacterSheetNegative"/> without "props", for a single-subject sheet whose
    /// character has canonical clothing/accessories/features - a "props" exclusion could suppress those signature items.
    /// </summary>
    public const string SingleCharacterNoOtherCharactersNegative = "other characters";

    /// <summary>
    /// Exclusions that are ALWAYS applied to an Environment reference (an empty
    /// establishing plate): anything that puts a subject into the location.
    /// </summary>
    public const string EnvironmentPlateNegative =
        "people, person, human, characters, figures, silhouettes, faces, crowd, animals as characters, text, watermark";

    /// <summary>The type-specific exclusion terms (see <see cref="CharacterSheetNegative"/> / <see cref="EnvironmentPlateNegative"/>).</summary>
    public static string TypeSpecificNegative(AssetReferenceType type) =>
        type == AssetReferenceType.Character ? CharacterSheetNegative : EnvironmentPlateNegative;

    /// <summary>
    /// Style negative + type-specific exclusions + the quality safety net, for
    /// callers that do not go through the LLM (the user-edited custom-prompt
    /// path and the deterministic Story-level reference prompts). Optionally
    /// includes an LLM-derived negative first. <paramref name="allowProps"/> only matters for a
    /// single-subject Character sheet and drops the "props" term (see <see cref="SingleCharacterNoOtherCharactersNegative"/>).
    /// </summary>
    public static string MergeWithQualityNegative(AssetReferenceType type, string? styleNegative, string? modelNegative = null, bool singleSubject = false, bool allowProps = false) =>
        MergeNegative(
            modelNegative,
            styleNegative,
            singleSubject && type == AssetReferenceType.Character
                ? $"{TypeSpecificNegative(type)}, {(allowProps ? SingleCharacterNoOtherCharactersNegative : SingleCharacterSheetNegative)}"
                : TypeSpecificNegative(type));

    private const string SystemPrompt = """
        You are a visual prompt engineer for a short-form video pipeline. You turn a video's ACTUAL script into one
        precise image-generation prompt for a reference image - a consistency anchor that later shots in the same
        video will be generated to match. You never invent a generic image for the niche; every choice must come
        from the story you are given.

        CRITICAL RULES - never violate these:
        1. SUBJECT LOCK: the main subject(s) explicitly named or clearly implied in the Story Context are the ONLY
           main subject(s) allowed. Preserve species, count, age and gender exactly as the story implies - a kitten
           stays a kitten (never a cat, dog, human or generic animal); "boy and kitten" means both, not one. Never
           substitute, add or remove a main character. If the story does not describe or need a person, do NOT
           invent a human presenter. (Exception: an "Environment" reference has NO character subject at all - see
           the Environment rules below.)
        2. STYLE LOCK: the given "Visual style" is mandatory and must never be contradicted - if it says 3D
           animated, cartoon, anime or illustration, never write "photorealistic" or "photograph"; if it says
           photorealistic or documentary, never write "cartoon", "anime" or "illustration".
        3. MOOD LOCK: infer the emotional tone from the Story Context (plus the Story type / Hook style / Emotion
           hints when given). For an "Environment" reference: dark, sad, lonely, mysterious or horror stories need
           low-key lighting, deep shadows and a muted or desaturated palette; cute, funny or playful stories need
           warm, bright-but-controlled lighting. Never default to bright and cheerful just because the niche sounds cute
           (e.g. cats/kittens) - the STORY's tone decides the mood, not the niche or subject species.
           For a "Character" reference the mood must NOT be expressed through the pose, expression, lighting or
           backdrop: the character sheet stays neutral (see the Character rules below) no matter how dark, dramatic
           or emotional the story is.
        4. You may enrich missing small details only when they do not contradict anything explicit in the story -
           missing information is never permission to change the subject, species, setting, action or tone.
        5. NO RIGID FRAMING TERMS: this prompt only ever generates a standalone identity/appearance reference, never
           a specific shot in the actual video - never write a fixed composition/camera term such as "centered
           shot", "center frame", "dead center" or similar. Future scenes will be framed however each individual
           shot needs (close-up, wide, off-center, over-the-shoulder, etc.), and locking composition here would
           fight every one of them. Describe the subject and its framing looseness (e.g. "clearly readable")
           without dictating where in the frame the subject sits.

        For a "Character" reference: a NEUTRAL IDENTITY STUDY (a character sheet), never a scene. Describe only the
        main subject(s)' appearance - species, build, face, fur/skin/hair, clothing, colors, distinguishing
        features and the mandatory art style. The staging is fixed regardless of the story: FULL BODY, head to toe
        (or nose to tail) fully visible and nothing cropped; neutral relaxed standing pose (an A-pose where the
        species allows it); facing straight toward the camera at flat eye level, looking at the camera with a neutral
        calm expression, the eyes clearly visible and the face unobstructed; against a seamless solid WHITE studio
        background with no scenery, floor texture, props, furniture, text or other characters; soft even lighting
        with no dramatic shadows. Nothing may hide an identity feature (face, ears, tail, markings, eye colour,
        hair), and never invent clothing, accessories or items the story does not state. Do NOT place the
        character in the story's setting, do NOT use the story's key-moment pose or expression, and do NOT let the
        story's mood pick a dark, dramatic, colored or scenic backdrop.
        For an "Environment" reference: an EMPTY PLATE - the location, atmosphere, lighting and color palette the
        story's scenes actually take place in (not a generic room or backdrop), as a wide/establishing view with NO
        characters, people, faces, figures, silhouettes, crowds or animals-as-characters anywhere in the frame and
        no foreground subject. The location itself is the only subject.

        Respond with ONLY one JSON object, no markdown fences, matching exactly this schema:
        {"analysis":{"mainSubjects":[string],"action":string,"environment":string,"emotion":string,"tone":string,"visualStyle":string,"lighting":string,"colorPalette":string,"composition":string},"prompt":string,"negativePrompt":string}

        Rules:
        - analysis.mainSubjects: the exact main subject(s), each a short noun phrase (species/role + 1-2
          distinguishing details), e.g. "small orange tabby kitten with green eyes" - never a human unless the
          story actually has one. For an "Environment" reference list the location/setting itself (never a person
          or character), e.g. "rain-slicked neon alley".
        - prompt: ONE finished, ready-to-use image-generation prompt in plain English (no headings, no labels, no
          JSON inside it). For a "Character" it describes the subject's appearance, the neutral standing pose, the
          seamless solid white studio background, soft even lighting and the art style, ending with a short
          sentence that locks exactly the main subject(s) the story names and adds nobody else (e.g. "Only the
          boy and the kitten appear, no additional characters. Plain white background, no scenery."). For an "Environment" it describes the empty
          location, atmosphere, lighting, color palette, art style and wide framing, ending with "Empty scene, no
          people, no characters."
        - analysis fields for a "Character": action = the neutral standing pose, environment = the plain
          white background, lighting = soft even studio lighting (not the story's mood lighting).
        - negativePrompt: comma-separated terms to exclude, derived from THIS story and style - exclude
          "human, person" only when no human belongs in the story; exclude "photorealistic, photograph" only when
          the requested style is not photorealistic; for an "Environment", exclude "bright cheerful lighting,
          oversaturated colors" only when the tone is dark or serious. The application ALWAYS adds the asset type's own exclusions
          itself (Character: backdrop/scenery/props terms; Environment: people/character terms), so do not spend
          words on those. Do not include a fixed list unrelated to this story.
        - No text of any kind outside the JSON object.
        """;

    private readonly ILlmProvider _llmProvider;

    public AssetReferencePromptAgent(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    private record RawAnalysis(
        List<string>? MainSubjects,
        string? Action,
        string? Environment,
        string? Emotion,
        string? Tone,
        string? VisualStyle,
        string? Lighting,
        string? ColorPalette,
        string? Composition);

    private record RawOutput(RawAnalysis? Analysis, string? Prompt, string? NegativePrompt);

    public async Task<AssetReferencePromptOutput> GenerateAsync(AssetReferencePromptAgentInput input, CancellationToken cancellationToken = default)
    {
        var topicLine = string.IsNullOrWhiteSpace(input.Topic) ? "" : $"Topic: {input.Topic.Trim()}\n";
        var nicheLine = string.IsNullOrWhiteSpace(input.Niche) ? "" : $"Niche: {input.Niche.Trim()}\n";

        var userPrompt = $"""
            Asset type: {input.AssetType}
            Video title: "{input.Title}"
            {topicLine}{nicheLine}Story type: {input.StoryType ?? "(not specified)"}
            Hook style: {input.HookStyle ?? "(not specified)"}
            Intended emotion: {input.Emotion ?? "(not specified)"}
            Visual style (mandatory, do not contradict): {input.StyleGuidance}
            Style exclusions to respect: {input.StyleNegativePrompt ?? "(none)"}
            Aspect ratio: {input.AspectRatio}

            Story Context (authoritative for subject, action, setting and tone):
            {input.StoryContext}

            Generate the {input.AssetType} reference now.
            """;

        var raw = await JsonAgentRunner.RunAsync<RawOutput>(
            _llmProvider,
            SystemPrompt,
            userPrompt,
            validate: IsUsable,
            cancellationToken);

        var analysis = new AssetReferenceVisualAnalysis(
            MainSubjects: raw.Analysis!.MainSubjects!.Select(s => s.Trim()).ToList(),
            Action: raw.Analysis.Action!.Trim(),
            Environment: raw.Analysis.Environment!.Trim(),
            Emotion: raw.Analysis.Emotion!.Trim(),
            Tone: raw.Analysis.Tone!.Trim(),
            VisualStyle: raw.Analysis.VisualStyle!.Trim(),
            Lighting: raw.Analysis.Lighting!.Trim(),
            ColorPalette: raw.Analysis.ColorPalette!.Trim(),
            Composition: raw.Analysis.Composition!.Trim());

        return new AssetReferencePromptOutput(
            analysis,
            raw.Prompt!.Trim(),
            MergeNegative(
                raw.NegativePrompt,
                input.StyleNegativePrompt,
                Enum.TryParse<AssetReferenceType>(input.AssetType, ignoreCase: true, out var assetType)
                    ? TypeSpecificNegative(assetType)
                    : null));
    }

    private static bool IsUsable(RawOutput output) =>
        !string.IsNullOrWhiteSpace(output.Prompt) &&
        output.Analysis?.MainSubjects is { Count: > 0 } subjects &&
        subjects.All(s => !string.IsNullOrWhiteSpace(s)) &&
        !string.IsNullOrWhiteSpace(output.Analysis.Action) &&
        !string.IsNullOrWhiteSpace(output.Analysis.Environment) &&
        !string.IsNullOrWhiteSpace(output.Analysis.Emotion) &&
        !string.IsNullOrWhiteSpace(output.Analysis.Tone) &&
        !string.IsNullOrWhiteSpace(output.Analysis.VisualStyle) &&
        !string.IsNullOrWhiteSpace(output.Analysis.Lighting) &&
        !string.IsNullOrWhiteSpace(output.Analysis.ColorPalette) &&
        !string.IsNullOrWhiteSpace(output.Analysis.Composition);

    /// <summary>Merges the model's own derived exclusions with the style preset's, the asset type's own deterministic exclusions and a fixed, subject-agnostic quality safety net - never a hardcoded species/style term that could fight the model's own (correct) choice.</summary>
    private static string MergeNegative(string? modelNegative, string? styleNegative, string? typeNegative)
    {
        var terms = new List<string>();

        void Add(string? source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;
            foreach (var term in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                {
                    terms.Add(term);
                }
            }
        }

        Add(modelNegative);
        Add(styleNegative);
        Add(typeNegative);
        Add(QualitySafetyNegative);
        return string.Join(", ", terms);
    }
}
