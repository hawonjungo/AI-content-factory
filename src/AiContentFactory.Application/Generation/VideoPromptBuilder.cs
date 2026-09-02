using System.Text.RegularExpressions;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Generation;

/// <param name="Action">
/// One specific, observable primary action for a ~8-second clip. Normalised by
/// the builder (single sentence, single trailing period); the prompt agent is
/// what makes it <em>specific</em>.
/// </param>
/// <param name="HasCharacterReference">A Character reference exists - add the character-consistency sentence.</param>
/// <param name="HasEnvironmentReference">An Environment reference exists - add the environment-consistency sentence.</param>
/// <param name="StyleGuidance">Project-level visual style; blank falls back to <see cref="VideoPromptBuilder.DefaultStyleGuidance"/>.</param>
public record VideoPromptSpec(
    string? Action,
    CameraMovement Camera,
    bool HasCharacterReference,
    bool HasEnvironmentReference,
    string? StyleGuidance,
    int DurationSeconds,
    string AspectRatio);

/// <summary>
/// The deterministic Scene-data -> final-video-prompt step. Given a normalised
/// primary action, one camera enum, which references exist and the project
/// style, it always produces the same prompt: <b>one clean English paragraph</b>
/// that can be pasted straight into Google Flow - no headings, labels,
/// separators, metadata or internal instructions.
///
/// Order of sentences: primary action, character consistency (only if a
/// Character reference exists), environment consistency (only if an Environment
/// reference exists), the single camera move, the project visual style, the
/// vertical format + length, and the on-screen-text restriction.
///
/// No randomness, no "A or B" camera wording, no per-scene restyling.
/// </summary>
public static class VideoPromptBuilder
{
    /// <summary>House visual style, matching the "photoreal-doc" preset - used when a project has no style guidance.</summary>
    public const string DefaultStyleGuidance =
        "Photorealistic documentary footage, natural daylight, realistic skin and materials, neutral color grade, 50mm lens, natural depth of field";

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

        if (spec.HasCharacterReference)
        {
            sentences.Add(
                "Keep the main character exactly consistent with the character reference — the same face, " +
                "body proportions, hair and clothing, with realistic anatomy and natural movement.");
        }

        if (spec.HasEnvironmentReference)
        {
            sentences.Add(
                "Keep the setting consistent with the environment reference — the same location, lighting, " +
                "colour palette and mood.");
        }

        sentences.Add(CameraToText(spec.Camera));
        sentences.Add(StyleSentence(spec.StyleGuidance));
        sentences.Add($"Vertical {AspectOrDefault(spec.AspectRatio)}, about {ClampDuration(spec.DurationSeconds)} seconds.");
        sentences.Add("No on-screen text, subtitles, captions, logos or watermarks.");

        return string.Join(" ", sentences);
    }

    /// <summary>One explicit camera sentence per movement - never a choice between two.</summary>
    public static string CameraToText(CameraMovement camera) => camera switch
    {
        CameraMovement.Static => "Locked-off static shot, no camera movement.",
        CameraMovement.SlowPushIn => "Slow, steady push-in toward the subject.",
        CameraMovement.SlowPullOut => "Slow, steady pull-out away from the subject.",
        CameraMovement.HandheldFollow => "Handheld camera following the subject at a steady distance.",
        CameraMovement.SideTracking => "Smooth lateral tracking shot moving parallel to the subject.",
        CameraMovement.ForwardTracking => "Smooth forward tracking shot moving with the subject.",
        CameraMovement.OverShoulder => "Over-the-shoulder shot framed just behind the subject.",
        _ => "Slow, subtle push-in toward the subject.", // Unspecified -> house default
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

    private static string AspectOrDefault(string? aspect) =>
        string.IsNullOrWhiteSpace(aspect) ? "9:16" : aspect.Trim();

    private static int ClampDuration(int seconds) =>
        seconds <= 0 ? 8 : Math.Clamp(seconds, MinClipSeconds, MaxClipSeconds);
}
