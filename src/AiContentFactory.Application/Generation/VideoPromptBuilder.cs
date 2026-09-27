using System.Text;
using System.Text.RegularExpressions;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Generation;

/// <param name="Action">
/// One specific, observable primary action for a ~8-second clip. Normalised by
/// the builder (single sentence, single trailing period); the prompt agent is
/// what makes it <em>specific</em>.
/// </param>
/// <param name="HasCharacterReference">A Character reference exists - add the character-consistency sentence.</param>
/// <param name="HasEnvironmentReference">An Environment reference exists - add the environment-consistency sentence.</param>
/// <param name="StyleGuidance">Project-level visual style; blank falls back to <see cref="VideoPromptBuilder.DefaultStyleGuidance"/>.</param>
/// <param name="CharacterLabels">
/// Real names of the character reference(s) selected for this specific scene
/// (e.g. <c>["Milo", "Mimi"]</c>) from the per-scene named-reference
/// selection (<c>SceneAssetGenerator.SelectReferencesForScene</c> /
/// <c>FlowGenerationPlanService</c>'s equivalent) - null/empty means no name
/// is known, so <see cref="VideoPromptBuilder.Build"/> falls back to today's
/// generic character-consistency sentence whenever <see cref="HasCharacterReference"/>
/// is still true (e.g. a non-Story project's classic single Character anchor,
/// which has no name). Never breaks the "no reference at all -&gt; no sentence"
/// case: with no names AND <see cref="HasCharacterReference"/> false, nothing
/// is added.
/// </param>
/// <param name="LocationLabel">
/// The real name of the Environment/location reference selected for this
/// specific scene (e.g. <c>"Hanoi Old Quarter"</c>), if one is known - null
/// means no location name is known, so no setting sentence/line is added
/// (the generic environment-consistency sentence in <see cref="VideoPromptBuilder.Build"/>
/// still fires exactly as before whenever <see cref="HasEnvironmentReference"/> is true).
/// </param>
/// <param name="CharacterVisualDescriptions">
/// <see cref="BuildNarrative"/>-only: name -&gt; appearance-description lookup
/// (e.g. <c>"Milo" -&gt; "orange tabby, one white paw, green eyes"</c>, from
/// the Story "bible"'s <c>StoryCharacter.VisualDescription</c>) so the
/// character's appositive clause can say what they actually look like, not
/// just their name - the Flow copy-paste workflow has no reference image
/// travelling with the text. Ignored by <see cref="Build"/> (the direct Veo
/// path already attaches the real reference image). Null/missing entries
/// simply fall back to the bare name - never an empty <c>()</c>.
/// </param>
/// <param name="LocationVisualDescription">
/// <see cref="BuildNarrative"/>-only counterpart of
/// <see cref="CharacterVisualDescriptions"/> for <see cref="LocationLabel"/>.
/// Ignored by <see cref="Build"/>.
/// </param>
/// <param name="CharacterBehaviorProfiles">
/// Name -&gt; opted-in <see cref="CharacterBehaviorProfile"/> lookup (e.g. from
/// <see cref="Stories.IStoryVisualContextResolver.GetCharacterBehaviorProfilesAsync"/>).
/// Unlike <see cref="CharacterVisualDescriptions"/> this is honoured by BOTH
/// <see cref="Build"/> and <see cref="BuildNarrative"/> - a behavior clause is
/// scene-specific instruction a static reference image can't convey, not
/// static appearance a reference image already carries. Only ever produces
/// text for a name present in <see cref="CharacterLabels"/> whose profile is
/// non-<see cref="CharacterBehaviorProfile.None"/> AND whose scene action
/// actually matches a relevant cue (see <see cref="CharacterBehaviorClauses"/>) -
/// null/missing entries add nothing, same "unaffected by default" contract as
/// every other optional field here.
/// </param>
/// <param name="Shot">
/// The scene's framing. <see cref="ShotSize.Unspecified"/> (the default, and
/// every scene prompted before framing existed) keeps the camera sentence
/// exactly as before; otherwise it leads the cinematography sentence
/// ("Close-up, slow subtle push-in.").
/// </param>
public record VideoPromptSpec(
    string? Action,
    CameraMovement Camera,
    bool HasCharacterReference,
    bool HasEnvironmentReference,
    string? StyleGuidance,
    int DurationSeconds,
    string AspectRatio,
    IReadOnlyList<string>? CharacterLabels = null,
    string? LocationLabel = null,
    IReadOnlyDictionary<string, string>? CharacterVisualDescriptions = null,
    string? LocationVisualDescription = null,
    IReadOnlyDictionary<string, CharacterBehaviorProfile>? CharacterBehaviorProfiles = null,
    ShotSize Shot = ShotSize.Unspecified)
{
    /// <summary>Non-null view of <see cref="CharacterLabels"/> for the builder to consume without a null-check at every call site.</summary>
    internal IReadOnlyList<string> CharacterLabelsOrEmpty => CharacterLabels ?? Array.Empty<string>();
}

/// <summary>
/// The deterministic Scene-data -> final-video-prompt step, shared by Veo
/// generation (<see cref="SceneAssetGenerator"/>, via <see cref="Build"/> -
/// one clean flowing paragraph, unchanged in shape) and the Google Flow
/// copy-paste plan (<see cref="FlowGenerationPlanService"/>, via
/// <see cref="BuildNarrative"/> - also a single flowing cinematic paragraph,
/// but composed with named-character appositive clauses and short inline
/// reference tags instead of <see cref="Build"/>'s generic "Consistent
/// character reference" sentence, since a human reads and pastes this text
/// manually into Flow rather than an API call receiving it verbatim).
/// Given a normalised primary action, one camera enum, which references
/// exist (plus their real names when the per-scene selection knows them) and
/// the project style, each method always produces the same output for the
/// same input - no randomness, no "A or B" camera wording, no per-scene
/// restyling.
///
/// <see cref="Build"/>'s sentence order: primary action, the setting sentence
/// (only when <see cref="VideoPromptSpec.LocationLabel"/> is known), character
/// consistency (only if a Character reference exists - named when
/// <see cref="VideoPromptSpec.CharacterLabels"/> is known, otherwise today's
/// generic sentence), environment consistency (only if an Environment
/// reference exists), the single camera move, the project visual style, the
/// vertical format + length, and the on-screen-text restriction. Each fixed
/// sentence is kept as short as possible - the action sentence (pulled
/// straight from the scene's own script/description) is the only one allowed
/// to be long.
/// </summary>
public static class VideoPromptBuilder
{
    /// <summary>House visual style, matching the "photoreal-doc" preset - used when a project has no style guidance.</summary>
    public const string DefaultStyleGuidance = "Photorealistic documentary, natural light, 50mm lens";

    /// <summary>Concrete single action used only when neither the agent nor the user has given one yet.</summary>
    public const string DefaultAction =
        "The main subject makes a small, natural movement and settles into a still, relaxed pose";

    private const int MinClipSeconds = 4;
    private const int MaxClipSeconds = 10;

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Build(VideoPromptSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var sentences = new List<string> { NormalizeAction(spec.Action) };

        if (!string.IsNullOrWhiteSpace(spec.LocationLabel))
        {
            sentences.Add($"Setting: {spec.LocationLabel!.Trim()}.");
        }

        var characterLabels = spec.CharacterLabelsOrEmpty;
        if (spec.HasCharacterReference || characterLabels.Count > 0)
        {
            sentences.Add(characterLabels.Count > 0
                ? $"Consistent character reference for {JoinNaturally(characterLabels)}: same face, body, hair, clothing, outfit as shown in the reference images."
                : "Consistent character reference: same face, body, hair, clothing.");
        }

        AddBehaviorSentences(sentences, characterLabels, spec.CharacterBehaviorProfiles, spec.Action);

        if (spec.HasEnvironmentReference)
        {
            sentences.Add("Consistent environment reference: same location, lighting, mood.");
        }

        sentences.Add(CinematographySentence(spec.Shot, spec.Camera));
        sentences.Add(StyleSentence(spec.StyleGuidance));
        sentences.Add($"Vertical {AspectOrDefault(spec.AspectRatio)}, {ClampDuration(spec.DurationSeconds)}s.");
        sentences.Add("No text, logos, watermarks.");

        return string.Join(" ", sentences);
    }

    /// <summary>
    /// The Google Flow copy-paste export's format: one flowing paragraph in
    /// the order Google's Veo prompting guide recommends - Cinematography,
    /// Subject + Action, Context, Style, then Audio - with named characters and
    /// their look woven in as short appositive clauses with inline reference
    /// tags, since the reference IMAGES never travel with copied text, only
    /// the words do.
    ///
    /// Natural prose only (no bracketed/labelled blocks - hands-on testing
    /// showed Flow's model reads those worse, see git history).
    ///
    /// Sentence order: the cinematography sentence (framing, when known, plus
    /// the single camera move); the subject/action sentence (named characters'
    /// appositive clauses + the scene's own single action, or the action alone
    /// when no character is known); the setting sentence when a location is
    /// known; opted-in behavior clauses; the project style with any camera-
    /// motion wording removed (the camera sentence alone owns motion, so a
    /// "handheld" style can never contradict a "static" or "push-in" camera),
    /// plus a "consistent character appearance" clause only when a character
    /// reference is in play for THIS scene; the audio sentence; the vertical
    /// aspect/duration sentence; and the fixed on-screen-text restriction.
    /// </summary>
    public static string BuildNarrative(VideoPromptSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var characterLabels = spec.CharacterLabelsOrEmpty;
        var hasCharacters = characterLabels.Count > 0;
        var action = NormalizeActionFull(spec.Action);

        var sentences = new List<string>
        {
            CinematographySentence(spec.Shot, spec.Camera),
            hasCharacters
                ? CharacterSentence(characterLabels, spec.CharacterVisualDescriptions, action)
                : action,
        };

        if (SettingSentence(spec.LocationLabel, spec.LocationVisualDescription) is { } setting)
        {
            sentences.Add(setting);
        }

        AddBehaviorSentences(sentences, characterLabels, spec.CharacterBehaviorProfiles, spec.Action);

        sentences.Add(StyleClosingSentence(RemoveCameraMotionWording(spec.StyleGuidance), hasCharacters || spec.HasCharacterReference));
        sentences.Add(AmbientAudioSentence);
        sentences.Add($"{AspectOrDefault(spec.AspectRatio)} vertical format, {ClampDuration(spec.DurationSeconds)}-second cinematic shot.");
        sentences.Add("No text, subtitles, logos, or watermarks.");

        return string.Join(" ", sentences);
    }

    /// <summary>
    /// The MOTION prompt for animating a first frame (Google Flow "Frames to
    /// Video", or the in-app Keyframe -&gt; video call). The frame already fixes
    /// who is on screen, what they look like, the setting, the light and the
    /// art style, so this describes ONLY what moves: the cinematography
    /// sentence, the scene's action, a "keep everything else exactly as in the
    /// frame" lock, opted-in behavior clauses, the audio sentence and the
    /// on-screen-text restriction. No names, no appearance, no style - those
    /// would compete with the image.
    /// </summary>
    public static string BuildMotion(VideoPromptSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var sentences = new List<string>
        {
            CinematographySentence(spec.Shot, spec.Camera),
            $"Starting exactly from the provided first frame: {LowercaseFirst(NormalizeActionFull(spec.Action))}",
            "Keep every character's appearance, the setting, the lighting and the visual style exactly as in the first frame - only the described movement changes.",
        };

        AddBehaviorSentences(sentences, spec.CharacterLabelsOrEmpty, spec.CharacterBehaviorProfiles, spec.Action);

        sentences.Add(AmbientAudioSentence);
        sentences.Add("No text, subtitles, logos, or watermarks.");
        return string.Join(" ", sentences);
    }

    /// <summary>
    /// Veo generates sound with every clip. The app lays its own narration and
    /// music over the video, so the clip should carry only the scene's natural
    /// sound - never invented speech (on-screen mouths moving to gibberish) or
    /// music that clashes with the soundtrack.
    /// </summary>
    public const string AmbientAudioSentence =
        "Audio: only the natural ambient sounds and sound effects of the scene, with no music and no spoken dialogue.";

    /// <summary>
    /// The location sentence, when a location is known: its name plus its
    /// short trait summary. Null when no location is known.
    /// </summary>
    private static string? SettingSentence(string? locationLabel, string? locationDescription)
    {
        if (string.IsNullOrWhiteSpace(locationLabel))
        {
            return null;
        }

        // Prose, not a "Setting:" label - Flow's model reads labelled lines worse.
        var sb = new StringBuilder("The scene takes place at ").Append(locationLabel.Trim());
        if (!string.IsNullOrWhiteSpace(locationDescription))
        {
            sb.Append(", ").Append(SummarizeDescription(locationDescription));
        }

        return sb.Append('.').ToString();
    }

    /// <summary>
    /// The cinematography sentence: framing first (when known) then the single
    /// camera move - e.g. "Close-up, slow subtle push-in." With no framing it
    /// is exactly <see cref="CameraToText"/>, so scenes prompted before framing
    /// existed keep their previous wording.
    /// </summary>
    public static string CinematographySentence(ShotSize shot, CameraMovement camera)
    {
        var framing = ShotToText(shot);
        if (framing is null)
        {
            return CameraToText(camera);
        }

        var move = camera == CameraMovement.Static
            ? "static camera with no movement"
            : LowercaseFirst(CameraToText(camera).TrimEnd('.'));
        return $"{framing}, {move}.";
    }

    /// <summary>The framing wording for a <see cref="ShotSize"/>; null for Unspecified.</summary>
    public static string? ShotToText(ShotSize shot) => shot switch
    {
        ShotSize.ExtremeWide => "Extreme wide shot",
        ShotSize.Wide => "Wide shot",
        ShotSize.Medium => "Medium shot",
        ShotSize.MediumCloseUp => "Medium close-up",
        ShotSize.CloseUp => "Close-up",
        ShotSize.ExtremeCloseUp => "Extreme close-up",
        _ => null,
    };

    private static readonly Regex CameraMotionWording = new(
        @"\b(hand-?held|shaky|steadicam|gimbal|static camera|locked[- ]off|camera (movement|motion|feel|shake)|dolly|pans?|panning|tracking|zoom(s|ed|ing)?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Drops the parts of a style string that describe camera MOTION (e.g. a
    /// preset's "handheld camera feel"). The camera sentence is the single
    /// source of motion; a style that also names a motion contradicts it
    /// whenever the scene's camera differs. A style with no motion wording is
    /// returned verbatim. Otherwise the text is split into sentences first
    /// (at a top-level "." or ";"), then each sentence into top-level comma
    /// parts (commas inside parentheses never split), and only the matching
    /// comma parts are removed - so a long prose style never loses a
    /// neighbouring sentence. Returns null when nothing is left, so the house
    /// default applies.
    /// </summary>
    public static string? RemoveCameraMotionWording(string? style)
    {
        if (string.IsNullOrWhiteSpace(style) || !CameraMotionWording.IsMatch(style))
        {
            return style;
        }

        var sentences = new List<string>();
        foreach (var (sentence, terminator) in SplitTopLevelSentences(style))
        {
            var kept = SplitTopLevel(sentence, ',')
                .Select(part => part.Trim())
                .Where(part => part.Length > 0 && !CameraMotionWording.IsMatch(part))
                .ToList();

            if (kept.Count > 0)
            {
                sentences.Add(string.Join(", ", kept) + terminator);
            }
        }

        var result = string.Join(" ", sentences).TrimEnd(';', ',', ' ');
        return result.Length == 0 ? null : result;
    }

    /// <summary>Splits at "." / ";" that sit outside parentheses and end a sentence (followed by whitespace or the end), keeping each terminator.</summary>
    private static IEnumerable<(string Sentence, string Terminator)> SplitTopLevelSentences(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (depth == 0 && (c == '.' || c == ';') && (i == text.Length - 1 || char.IsWhiteSpace(text[i + 1])))
            {
                yield return (text[start..i], c.ToString());
                start = i + 1;
            }
        }

        if (start < text.Length && text[start..].Trim().Length > 0)
        {
            yield return (text[start..], string.Empty);
        }
    }

    /// <summary>Splits at <paramref name="separator"/> only outside parentheses.</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (c == separator && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        yield return text[start..];
    }

    /// <summary>
    /// One sentence naming every known character with a short appositive
    /// trait clause and inline reference tag, followed by the scene's own
    /// (single, shared) action text - never inventing a separate action per
    /// character, since the source data only ever carries one action for the
    /// whole scene.
    /// </summary>
    private static string CharacterSentence(IReadOnlyList<string> names, IReadOnlyDictionary<string, string>? descriptions, string action)
    {
        var clauses = names.Select(name => CharacterClause(name, DescriptionFor(name, descriptions))).ToList();
        var joined = clauses.Count switch
        {
            1 => clauses[0],
            2 => $"{clauses[0]}, and {clauses[1]}",
            _ => string.Join(", ", clauses.Take(clauses.Count - 1)) + ", and " + clauses[^1],
        };

        var body = action.TrimEnd();
        var terminator = body.Length > 0 && body[^1] is '!' or '?' ? string.Empty : ".";
        return $"{joined}, {LowercaseFirst(body.TrimEnd('.'))}{terminator}";
    }

    /// <summary>
    /// One named character's appositive clause: <c>"Milo, orange tabby, one
    /// white paw, green eyes (refer to the attached Milo reference)"</c> when
    /// a description is known, otherwise just <c>"Milo (refer to the
    /// attached Milo reference)"</c> - never an empty/placeholder trait tag.
    /// A named label always implies a real approved reference image in the
    /// actual pipeline (<see cref="FlowGenerationPlanService"/> only ever
    /// resolves <c>CharacterLabels</c> from approved, tagged/matched
    /// references - see <see cref="ReferenceMatcher"/>), so the inline
    /// reference tag is always safe to add for a known name.
    /// </summary>
    private static string CharacterClause(string name, string? description)
    {
        var refTag = $"(refer to the attached {name} reference)";
        return string.IsNullOrWhiteSpace(description)
            ? $"{name} {refTag}"
            : $"{name}, {SummarizeDescription(description!)} {refTag}";
    }

    private static string LowercaseFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>
    /// <see cref="BuildNarrative"/>'s style sentence: the project style (or
    /// the house default), plus a "consistent character appearance
    /// throughout the shot" clause whenever a character reference is in play
    /// (named or the classic single unnamed anchor) - matching the closing
    /// clause of both of the user's reference examples.
    /// </summary>
    private static string StyleClosingSentence(string? style, bool includeCharacterConsistency)
    {
        var s = (string.IsNullOrWhiteSpace(style) ? DefaultStyleGuidance : style.Trim()).TrimEnd('.', ' ');
        if (s.Length == 0)
        {
            s = DefaultStyleGuidance;
        }

        if (includeCharacterConsistency)
        {
            s += ", consistent character appearance throughout the shot";
        }

        return char.ToUpperInvariant(s[0]) + s[1..] + ".";
    }

    /// <summary>One explicit camera sentence per movement - never a choice between two.</summary>
    public static string CameraToText(CameraMovement camera) => camera switch
    {
        CameraMovement.Static => "Static shot, no camera movement.",
        CameraMovement.SlowPushIn => "Slow subtle push-in.",
        CameraMovement.SlowPullOut => "Slow subtle pull-out.",
        CameraMovement.HandheldFollow => "Handheld follow at a steady distance.",
        CameraMovement.SideTracking => "Smooth lateral tracking shot.",
        CameraMovement.ForwardTracking => "Smooth forward tracking shot.",
        CameraMovement.OverShoulder => "Over-the-shoulder framing.",
        _ => "Slow subtle push-in.", // Unspecified -> house default
    };

    /// <summary>
    /// Best-effort mapping of a legacy free-form <c>CameraDirection</c> string onto
    /// the structured enum, so scenes created before the enum existed still get a
    /// deterministic camera line. Unknown wording -> <see cref="CameraMovement.Unspecified"/>.
    /// </summary>
    public static CameraMovement ParseCamera(string? freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return CameraMovement.Unspecified;
        }

        var t = freeText.ToLowerInvariant();

        if (Enum.TryParse<CameraMovement>(freeText.Replace(" ", string.Empty), ignoreCase: true, out var exact))
        {
            return exact;
        }

        if (t.Contains("over the shoulder") || t.Contains("over-the-shoulder") || t.Contains("shoulder")) return CameraMovement.OverShoulder;
        if (t.Contains("static") || t.Contains("locked") || t.Contains("lock-off") || t.Contains("no movement") || t.Contains("still")) return CameraMovement.Static;
        if (t.Contains("pull out") || t.Contains("pull-out") || t.Contains("pullback") || t.Contains("pull back") || t.Contains("zoom out") || t.Contains("dolly out")) return CameraMovement.SlowPullOut;
        if (t.Contains("push in") || t.Contains("push-in") || t.Contains("pushin") || t.Contains("zoom in") || t.Contains("dolly in")) return CameraMovement.SlowPushIn;
        if (t.Contains("handheld") || t.Contains("hand-held") || t.Contains("follow")) return CameraMovement.HandheldFollow;
        if (t.Contains("forward") && (t.Contains("track") || t.Contains("dolly") || t.Contains("move"))) return CameraMovement.ForwardTracking;
        if (t.Contains("side") || t.Contains("lateral") || t.Contains("truck") || (t.Contains("track") && t.Contains("parallel"))) return CameraMovement.SideTracking;
        if (t.Contains("track") || t.Contains("tracking")) return CameraMovement.ForwardTracking;

        return CameraMovement.Unspecified;
    }

    /// <summary>
    /// Collapses a possibly multi-action / multi-sentence description into one
    /// primary action sentence with a single trailing period. The prompt agent
    /// is the real normaliser; this is the deterministic backstop.
    /// </summary>
    public static string NormalizeAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return EnsureSentence(DefaultAction);
        }

        var text = Whitespace.Replace(action.Trim(), " ");

        // Drop obvious sequencing: "walk in, then pick up the book, then leave" -> "walk in".
        foreach (var sep in new[] { " then ", ", then ", "; ", " and then ", " → ", " -> " })
        {
            var idx = text.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                text = text[..idx];
            }
        }

        // If several sentences remain, keep the first.
        var firstStop = text.IndexOfAny(new[] { '.', '!', '?' });
        if (firstStop >= 0 && firstStop < text.Length - 1)
        {
            text = text[..firstStop];
        }

        return EnsureSentence(text);
    }

    /// <summary>
    /// The <see cref="BuildNarrative"/>-only counterpart of <see cref="NormalizeAction"/>:
    /// same whitespace collapsing, capitalisation and trailing period, but
    /// deliberately does NOT collapse multiple sentences down to the first
    /// one, and does NOT cut at "then"/sequencing words - the Flow export is
    /// read by a human, not fed straight into a single-action video-generation
    /// call, so the fuller, richer action text is more useful here than the
    /// direct-API paragraph's single-beat discipline.
    /// </summary>
    private static string NormalizeActionFull(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return EnsureSentence(DefaultAction);
        }

        var text = Whitespace.Replace(action.Trim(), " ").TrimEnd();
        if (text.Length == 0)
        {
            return EnsureSentence(DefaultAction);
        }

        var capitalised = char.ToUpperInvariant(text[0]) + text[1..];
        return EndsWithTerminalPunctuation(capitalised) ? capitalised : capitalised + ".";
    }

    private static bool EndsWithTerminalPunctuation(string text) =>
        text.Length > 0 && text[^1] is '.' or '!' or '?';

    private static string EnsureSentence(string text)
    {
        text = text.Trim().TrimEnd('.', '!', '?', ',', ';', ' ');
        if (text.Length == 0)
        {
            text = DefaultAction;
        }

        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>The project style as one capitalised sentence with a single trailing period.</summary>
    private static string StyleSentence(string? style)
    {
        var s = (string.IsNullOrWhiteSpace(style) ? DefaultStyleGuidance : style.Trim()).TrimEnd('.', ' ');
        if (s.Length == 0)
        {
            s = DefaultStyleGuidance;
        }

        return char.ToUpperInvariant(s[0]) + s[1..] + ".";
    }

    /// <summary>Character budget for <see cref="SummarizeDescription"/> before it truncates at a clause boundary.</summary>
    private const int MaxDescriptionLength = 80;

    /// <summary>
    /// <see cref="BuildNarrative"/>-only: shortens a possibly long,
    /// sentence-like <c>VisualDescription</c> (e.g. "A fluffy orange tabby
    /// cat with bright green eyes, a white chest patch, and a slightly
    /// chubby belly from all the snacking.") down to a short trait tag
    /// suitable for an appositive clause, rather than a full run-on
    /// sentence. Short descriptions (already trait-list-shaped, e.g. from the
    /// Story bible) pass through unchanged. Deterministic: same input always
    /// produces the same output.
    /// </summary>
    private static string SummarizeDescription(string description)
    {
        var text = description.Trim().TrimEnd('.', ' ');
        if (text.Length <= MaxDescriptionLength)
        {
            return text;
        }

        var truncated = text[..MaxDescriptionLength];

        // Prefer cutting at a clause boundary (comma) so the result still
        // reads like a trait list; fall back to the last whole word.
        var lastComma = truncated.LastIndexOf(',');
        if (lastComma > 20)
        {
            truncated = truncated[..lastComma];
        }
        else
        {
            var lastSpace = truncated.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                truncated = truncated[..lastSpace];
            }
        }

        return truncated.TrimEnd(',', ' ');
    }

    private static string? DescriptionFor(string name, IReadOnlyDictionary<string, string>? descriptions) =>
        descriptions is not null && descriptions.TryGetValue(name, out var description) ? description : null;

    /// <summary>
    /// Shared by <see cref="Build"/> and <see cref="BuildNarrative"/>: appends
    /// one sentence per named character whose <see cref="CharacterBehaviorProfile"/>
    /// opted in AND whose scene action text actually matched a relevant cue
    /// (see <see cref="CharacterBehaviorClauses.For"/>) - never for an unnamed
    /// scene (no <paramref name="characterLabels"/> means no one to attribute
    /// behavior to), never unconditionally for every scene featuring an
    /// opted-in character. Deduplicated - the clause text is species-generic,
    /// not per-character-named, so two opted-in characters sharing the same
    /// profile and the same scene action (e.g. Milo and Mimi both
    /// AnthropomorphicCat in one shared shot) would otherwise get the
    /// identical sentence added twice.
    /// </summary>
    private static void AddBehaviorSentences(
        List<string> sentences, IReadOnlyList<string> characterLabels,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles, string? action)
    {
        if (behaviorProfiles is null || characterLabels.Count == 0)
        {
            return;
        }

        var addedClauses = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in characterLabels)
        {
            if (behaviorProfiles.TryGetValue(name, out var profile) && profile != CharacterBehaviorProfile.None)
            {
                var clause = CharacterBehaviorClauses.For(profile, action);
                if (clause is not null && addedClauses.Add(clause))
                {
                    sentences.Add(clause);
                }
            }
        }
    }

    /// <summary>Joins names the way a person would say them aloud: "Milo", "Milo and Mimi", "Milo, Mimi and X".</summary>
    private static string JoinNaturally(IReadOnlyList<string> names) => names.Count switch
    {
        <= 1 => names.Count == 1 ? names[0] : string.Empty,
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private static string AspectOrDefault(string? aspect) =>
        string.IsNullOrWhiteSpace(aspect) ? "9:16" : aspect.Trim();

    private static int ClampDuration(int seconds) =>
        seconds <= 0 ? 8 : Math.Clamp(seconds, MinClipSeconds, MaxClipSeconds);
}
