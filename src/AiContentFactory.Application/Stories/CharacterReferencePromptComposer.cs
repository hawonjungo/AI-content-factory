using System.Text.RegularExpressions;
using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Presets;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

/// <summary>
/// Where a composed character-reference prompt is going. Only <see cref="InApp"/>
/// is executed by this application (Gemini); every other target is a plain
/// TEXT EXPORT the user pastes into that tool by hand - this app never calls
/// those tools, the enum only decides how the same content is formatted.
/// </summary>
public enum ReferencePromptTarget
{
    /// <summary>The in-app image provider: positive prompt plus a separate negative prompt.</summary>
    InApp = 0,

    /// <summary>One self-contained natural-language text (exclusions folded in) for any tool.</summary>
    Generic = 1,

    /// <summary>Google Flow (manual copy/paste): same as <see cref="Generic"/> plus a note on attaching the approved image as an ingredient.</summary>
    Flow = 2,

    /// <summary>Midjourney: one text ending with <c>--ar 2:3 --no ...</c>.</summary>
    Midjourney = 3,

    /// <summary>DALL-E: one self-contained text (no negative field).</summary>
    Dalle = 4,

    /// <summary>FLUX: one self-contained text (no negative field).</summary>
    Flux = 5,
}

/// <summary>
/// Everything the composer is allowed to know about a character - deliberately
/// a plain value so the composer stays pure and testable without entities.
/// </summary>
/// <param name="Species">Canonical species/breed text; null/blank when unknown. Ignored for <see cref="CharacterKind.Human"/>.</param>
/// <param name="Appearance">Canonical appearance text - a character's VisualDescription ONLY. Its Description is a role/personality summary and is never presented as appearance.</param>
/// <param name="AnthropomorphicOptIn">
/// The ONLY switch that can produce anthropomorphic wording. The caller decides
/// it (see <see cref="FromCharacter"/>); the composer never infers it from the
/// species, name or appearance text.
/// </param>
/// <param name="SeriesLook">The Story style's LOOK-ONLY wording (art style/medium/materials/colour grade); null = no style line at all.</param>
/// <param name="SeriesLookNegative">The Story style's look-only exclusions; merged into the negative list once.</param>
public sealed record CharacterReferenceSpec(
    string Name,
    string? Species,
    CharacterKind Kind,
    string? Appearance,
    string? ClothingAndAccessories,
    string? DistinctiveFeatures,
    bool AnthropomorphicOptIn,
    string? SeriesLook,
    string? SeriesLookNegative)
{
    /// <summary>
    /// True when there is an appearance text, or a species that is actually used
    /// (a <see cref="CharacterKind.Human"/> prompt ignores the species, so a stale
    /// species cannot satisfy the gate) - the minimum for a reference image to be
    /// worth paying for. A name-only prompt would make the model invent the whole character.
    /// </summary>
    public bool HasEnoughToGenerate =>
        !string.IsNullOrWhiteSpace(Appearance) || (!string.IsNullOrWhiteSpace(Species) && Kind != CharacterKind.Human);

    /// <summary>True when the character has canonical clothing/accessories or distinctive features - signature items that a "props" exclusion could suppress.</summary>
    public bool HasSignatureItems =>
        !string.IsNullOrWhiteSpace(ClothingAndAccessories) || !string.IsNullOrWhiteSpace(DistinctiveFeatures);

    /// <summary>
    /// Anthropomorphic wording is opt-in per character: an explicit
    /// <see cref="CharacterKind.AnthropomorphicAnimal"/> kind, or - only while the
    /// kind is still <see cref="CharacterKind.Unspecified"/> - the scoped
    /// <see cref="CharacterBehaviorProfile.AnthropomorphicCat"/> profile (older data
    /// that predates <see cref="CharacterKind"/>). An explicit Human/Animal/Other kind
    /// always wins over the profile. Never a keyword match on a species or a name.
    /// </summary>
    public static bool OptsIntoAnthropomorphic(CharacterKind kind, CharacterBehaviorProfile behaviorProfile) =>
        kind == CharacterKind.AnthropomorphicAnimal ||
        (kind == CharacterKind.Unspecified && behaviorProfile == CharacterBehaviorProfile.AnthropomorphicCat);

    /// <summary>Builds the spec from a stored character and the Story's style (null when the Story has none).</summary>
    public static CharacterReferenceSpec FromCharacter(StoryCharacter character, StylePreset? style) => new(
        character.Name,
        character.Species,
        character.Kind,
        character.VisualDescription,
        character.ClothingAndAccessories,
        character.DistinctiveFeatures,
        OptsIntoAnthropomorphic(character.Kind, character.BehaviorProfile),
        style?.ReferenceLookGuidance,
        style?.ReferenceNegativePrompt);
}

/// <param name="NegativePrompt">Only set for <see cref="ReferencePromptTarget.InApp"/>; every other target folds the exclusions into <paramref name="Prompt"/>.</param>
/// <param name="MissingFields">Stable camelCase ids of canonical inputs that are absent (advisory, never blocking).</param>
/// <param name="Notes">Short Vietnamese usage notes for the UI.</param>
public sealed record CharacterReferencePrompt(
    string Prompt,
    string? NegativePrompt,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> Notes);

/// <summary>
/// Deterministic, dependency-free composer of the standardized CHARACTER
/// REFERENCE prompt: full body, neutral A-pose, straight-on, solid white
/// background, soft even light. No LLM, no I/O. Everything species/kind
/// specific is decided ONLY by the <see cref="CharacterReferenceSpec"/> (never a
/// global keyword replacement), and no character or species name is hardcoded.
/// Never uses gendered pronouns, and never contains scene actions, dialogue,
/// video motion or camera movement - this image is an identity anchor.
/// </summary>
public static class CharacterReferencePromptComposer
{
    public const string FieldAppearance = "appearance";
    public const string FieldSpecies = "species";
    public const string FieldClothingAndAccessories = "clothingAndAccessories";
    public const string FieldDistinctiveFeatures = "distinctiveFeatures";

    private const string MidjourneyParameters = "--ar 2:3";

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DoubleDash = new(@"-{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses a route/query value (case-insensitive). Rejects blank, numeric and unknown values.</summary>
    public static bool TryParseTarget(string? value, out ReferencePromptTarget target)
    {
        target = ReferencePromptTarget.Generic;
        // Enum.TryParse also accepts numbers and comma-joined names ("generic,flow" would parse as Midjourney).
        if (string.IsNullOrWhiteSpace(value) || value.Contains(',') || int.TryParse(value, out _))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out target) && Enum.IsDefined(target);
    }

    /// <summary>The wire name of a target: lower-case, e.g. "midjourney".</summary>
    public static string TargetId(ReferencePromptTarget target) => target.ToString().ToLowerInvariant();

    /// <summary>Advisory list of absent canonical inputs. Species is not applicable to a human.</summary>
    public static IReadOnlyList<string> GetMissingFields(CharacterReferenceSpec spec)
    {
        var missing = new List<string>();
        if (IsBlank(spec.Appearance))
        {
            missing.Add(FieldAppearance);
        }

        if (IsBlank(spec.Species) && spec.Kind != CharacterKind.Human)
        {
            missing.Add(FieldSpecies);
        }

        if (IsBlank(spec.ClothingAndAccessories))
        {
            missing.Add(FieldClothingAndAccessories);
        }

        if (IsBlank(spec.DistinctiveFeatures))
        {
            missing.Add(FieldDistinctiveFeatures);
        }

        return missing;
    }

    public static CharacterReferencePrompt Compose(CharacterReferenceSpec spec, ReferencePromptTarget target)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var positive = BuildPositivePrompt(spec);
        var negative = BuildNegativePrompt(spec);
        var missing = GetMissingFields(spec);

        string prompt;
        string? negativePrompt = null;
        switch (target)
        {
            case ReferencePromptTarget.InApp:
                prompt = positive;
                negativePrompt = negative;
                break;
            case ReferencePromptTarget.Midjourney:
                prompt = $"{positive} {MidjourneyParameters} --no {negative}";
                break;
            case ReferencePromptTarget.Generic:
            case ReferencePromptTarget.Flow:
            case ReferencePromptTarget.Dalle:
            case ReferencePromptTarget.Flux:
                prompt = $"{positive} Avoid: {negative}.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown reference prompt target.");
        }

        return new CharacterReferencePrompt(prompt, negativePrompt, missing, BuildNotes(target));
    }

    /// <summary>
    /// The single negative list: the shared Character-sheet terms (background,
    /// pose/composition, quality net - owned by <see cref="AssetReferencePromptAgent"/>)
    /// plus the Story style's own look-only exclusions, de-duplicated by the
    /// same merge every other reference path uses. "props" is left out when the
    /// character has canonical clothing/accessories/features, so signature items
    /// are not suppressed.
    /// </summary>
    private static string BuildNegativePrompt(CharacterReferenceSpec spec) =>
        AssetReferencePromptAgent.MergeWithQualityNegative(
            AssetReferenceType.Character,
            Clean(spec.SeriesLookNegative),
            modelNegative: ResolveBranch(spec) == Branch.Animal ? NaturalAnimalNegative : null,
            singleSubject: true,
            allowProps: spec.HasSignatureItems);

    /// <summary>
    /// Extra exclusions for a natural (non-opted-in) animal: image models readily turn a named animal
    /// (especially a mythological-sounding name) into a person-like figure unless told not to.
    /// </summary>
    private const string NaturalAnimalNegative =
        "humanoid body, human torso, human face, human hands, standing on hind legs, bipedal pose, deity figure, mascot costume";

    private enum Branch
    {
        Anthropomorphic,
        Animal,
        Human,
        Neutral,
    }

    private static Branch ResolveBranch(CharacterReferenceSpec spec)
    {
        // The opt-in flag is the only way into anthropomorphic wording.
        if (spec.AnthropomorphicOptIn)
        {
            return Branch.Anthropomorphic;
        }

        return spec.Kind switch
        {
            CharacterKind.Animal => Branch.Animal,
            CharacterKind.Human => Branch.Human,
            _ => Branch.Neutral,
        };
    }

    private static string BuildPositivePrompt(CharacterReferenceSpec spec)
    {
        var branch = ResolveBranch(spec);
        var name = IsBlank(spec.Name) ? "the character" : Clean(spec.Name)!;
        var species = branch == Branch.Human ? null : Clean(spec.Species);
        var sentences = new List<string>();

        // 1. Identity: who/what the character is.
        sentences.Add(branch switch
        {
            Branch.Anthropomorphic => species is null
                ? $"Full-body character reference of {name}, an anthropomorphic animal character."
                : $"Full-body character reference of {name}, an anthropomorphic {species} character.",
            Branch.Animal => species is null
                ? $"Full-body character reference of {name}, an animal."
                : $"Full-body character reference of {name}, {WithArticle(species)}.",
            Branch.Human => $"Full-body character reference of {name}, a human character.",
            _ => species is null
                ? $"Full-body character reference of {name}."
                : $"Full-body character reference of {name}, who is {WithArticle(species)}.",
        });

        // A natural animal must stay an ordinary animal: a name alone (e.g. a mythological one) must not
        // pull the model toward a person-like figure. Worded without the words the anthropomorphic branch owns.
        if (branch == Branch.Animal)
        {
            sentences.Add("It is depicted as a real, ordinary animal with the natural anatomy, proportions and posture of its species - not a person-like figure, deity, mascot or costumed character.");
        }
        else if (branch == Branch.Neutral)
        {
            sentences.Add("Its anatomy stays exactly as described, without making it more person-like than described.");
        }

        // 2. Canonical appearance and distinctive features, verbatim from the data.
        if (Clean(spec.Appearance) is { } appearance)
        {
            sentences.Add($"Canonical appearance: {appearance}.");
        }

        if (Clean(spec.DistinctiveFeatures) is { } features)
        {
            sentences.Add($"Distinctive features, exactly as described: {features}.");
        }

        sentences.Add(branch switch
        {
            Branch.Human => "The body proportions, hair, skin tone, eye colour and every distinguishing mark are clearly visible and unobstructed.",
            Branch.Anthropomorphic or Branch.Animal => "The body proportions, ears, tail, markings, fur or skin pattern and eye colour are clearly visible and unobstructed.",
            _ => "The silhouette, body proportions, markings, colouring and eye colour are clearly visible and unobstructed.",
        });

        // 3. Clothing: exactly as listed, nothing invented.
        if (Clean(spec.ClothingAndAccessories) is { } clothing)
        {
            sentences.Add($"Clothing and accessories, exactly as listed (including any shoes and signature items): {clothing}. All of them are clearly visible.");
            sentences.Add("Do not add clothing, accessories or items that are not listed.");
        }
        else
        {
            sentences.Add("Nothing is added beyond the description: no extra clothing, accessories or items.");
        }

        // 4. Framing and pose.
        sentences.Add(branch switch
        {
            Branch.Animal => "The entire head and all paws are fully visible and touching the ground, with the whole body centered in the frame, space around it and nothing cropped.",
            Branch.Neutral => "The entire head and the whole body down to the feet or base are fully visible and touching the ground, centered in the frame with space around it and nothing cropped.",
            _ => "The entire head and both feet are fully visible and touching the ground, with the whole figure centered in the frame, space around it and nothing cropped.",
        });

        sentences.Add(branch switch
        {
            Branch.Anthropomorphic => "The character stands upright on two legs in a neutral, relaxed A-pose with arms slightly away from the body, facing straight toward the camera at flat, straight-on eye level.",
            Branch.Animal => "The character stands in a natural, neutral animal stance with natural anatomy, body and head facing straight toward the camera at flat, straight-on eye level.",
            Branch.Human => "The character stands upright in a neutral, relaxed A-pose with arms slightly away from the body, facing straight toward the camera at flat, straight-on eye level.",
            _ => "The character is shown in a neutral, relaxed standing pose that is natural for its body, facing straight toward the camera at flat, straight-on eye level.",
        });

        sentences.Add("The character looks directly at the camera with a neutral expression; the eyes are clearly visible and the face is unobstructed, with no exaggerated emotion and no extreme head rotation.");

        sentences.Add(branch switch
        {
            Branch.Animal => "The pose stays static and neutral: not sitting, crouching, lying down or leaning, and no dynamic pose, extreme perspective, gestures or interaction with objects.",
            _ => "The pose stays static and neutral: not sitting, crouching, leaning or with crossed arms, and no dynamic pose, extreme perspective, gestures or interaction with objects.",
        });

        // 5. Background, lighting, quality.
        sentences.Add("Seamless solid white background: plain, clean and uninterrupted, with no scenery, furniture, props, food, patterns, text, logos or watermark, so the character is easy to isolate.");
        sentences.Add("Soft, even studio lighting with minimal harsh shadows; no dramatic coloured light and no strong rim light.");
        var seriesLook = Clean(spec.SeriesLook);

        // "clean studio render" names a medium, so it is only added when there is no series look
        // (a look string already defines the medium and the two must not fight).
        sentences.Add(seriesLook is null ? "High detail, sharp features, clean studio render." : "High detail, sharp features.");

        // 6. Series look, once and only when the Story has a style.
        if (seriesLook is not null)
        {
            sentences.Add($"Rendering style (look only): {seriesLook}.");
        }

        return string.Join(' ', sentences);
    }

    private static IReadOnlyList<string> BuildNotes(ReferencePromptTarget target)
    {
        var notes = new List<string>
        {
            "Ảnh tham chiếu giúp tăng độ nhất quán nhưng không đảm bảo nhất quán tuyệt đối. Việc prompt/ảnh được gửi đi, được công cụ thực sự sử dụng và kết quả đầu ra nhất quán là ba việc khác nhau - luôn kiểm tra ảnh kết quả."
        };

        switch (target)
        {
            case ReferencePromptTarget.InApp:
                notes.Add("Prompt và negative prompt được gửi riêng cho trình tạo ảnh trong app.");
                break;
            case ReferencePromptTarget.Generic:
                notes.Add("Văn bản tự chứa: các mục loại trừ đã được gộp vào cuối prompt vì phần lớn công cụ không có ô negative đáng tin cậy.");
                break;
            case ReferencePromptTarget.Flow:
                notes.Add("Google Flow: đính kèm ảnh tham chiếu đã duyệt làm ingredient. Các mục loại trừ được gộp vào văn bản, không dùng ô negative riêng.");
                break;
            case ReferencePromptTarget.Midjourney:
                notes.Add("Midjourney: tham số --ar 2:3 và --no được thêm ở cuối prompt. Cú pháp tham số có thể thay đổi theo phiên bản - hãy kiểm tra lại tài liệu hiện hành trước khi dùng.");
                break;
            case ReferencePromptTarget.Dalle:
                notes.Add("DALL-E không có ô negative prompt: các mục loại trừ được gộp vào văn bản và có thể không được tuân thủ tuyệt đối.");
                break;
            case ReferencePromptTarget.Flux:
                notes.Add("FLUX không có ô negative prompt: các mục loại trừ được gộp vào văn bản và có thể không được tuân thủ tuyệt đối.");
                break;
        }

        return notes;
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Normalises a user-provided fragment for embedding: single line, single
    /// spaces, no trailing sentence punctuation (the composer adds its own) and
    /// no "--" runs (which Midjourney would read as parameters).
    /// </summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = DoubleDash.Replace(Whitespace.Replace(value, " ").Trim(), "-");
        text = text.TrimEnd('.', ',', ';', ' ');
        return text.Length == 0 ? null : text;
    }

    private static string WithArticle(string noun)
    {
        var first = char.ToLowerInvariant(noun[0]);
        return first is 'a' or 'e' or 'i' or 'o' or 'u' ? $"an {noun}" : $"a {noun}";
    }
}
