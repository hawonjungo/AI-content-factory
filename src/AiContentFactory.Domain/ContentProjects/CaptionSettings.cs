using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.ContentProjects;

public enum CaptionPosition
{
    Bottom = 0,
    Center = 1,
    Top = 2
}

public enum CaptionAnimation
{
    None = 0,
    FadeIn = 1,
    PopIn = 2,
    SlideUp = 3
}

/// <summary>
/// Everything the renderer needs to draw on-screen text, owned by the
/// ContentProject and persisted as a single jsonb column. A caption preset
/// seeds these values; the user is then free to change any of them, which is
/// why this is stored per-project rather than resolved from the preset
/// catalog at render time the way style/voice presets are.
///
/// These map onto ASS/libass style fields rather than SRT - SRT can't express
/// per-cue position, karaoke highlighting, or animation, so none of this
/// would survive a round-trip through it.
/// </summary>
public class CaptionSettings
{
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Must be a family libass can actually find inside the API container -
    /// see the fonts-* packages installed in the Dockerfile. An unknown family
    /// silently falls back to whatever fontconfig picks, which for Vietnamese
    /// diacritics is often tofu boxes.
    /// </summary>
    public string FontFamily { get; private set; } = "DejaVu Sans";

    /// <summary>Points, measured against the 1080x1920 render canvas.</summary>
    public int FontSizePt { get; private set; } = 72;

    public string PrimaryColor { get; private set; } = "#FFFFFF";

    /// <summary>Colour a word turns while it is being spoken (karaoke only).</summary>
    public string HighlightColor { get; private set; } = "#FFD400";

    public string OutlineColor { get; private set; } = "#000000";

    public double OutlineWidth { get; private set; } = 3;

    public double ShadowDepth { get; private set; }

    public bool Bold { get; private set; } = true;

    public bool Uppercase { get; private set; }

    public CaptionPosition Position { get; private set; } = CaptionPosition.Bottom;

    /// <summary>Distance from the anchored edge (top or bottom); ignored for Center.</summary>
    public int MarginVerticalPx { get; private set; } = 220;

    /// <summary>How many words appear on screen at once. Short-form captions are typically 3-5.</summary>
    public int MaxWordsPerCue { get; private set; } = 4;

    public CaptionAnimation Animation { get; private set; } = CaptionAnimation.PopIn;

    /// <summary>Highlight each word as it is spoken instead of showing the whole cue in one colour.</summary>
    public bool Karaoke { get; private set; } = true;

    private CaptionSettings()
    {
        // EF Core / JSON deserialization
    }

    public static CaptionSettings Default() => new();

    public static CaptionSettings Create(
        bool enabled,
        string fontFamily,
        int fontSizePt,
        string primaryColor,
        string highlightColor,
        string outlineColor,
        double outlineWidth,
        double shadowDepth,
        bool bold,
        bool uppercase,
        CaptionPosition position,
        int marginVerticalPx,
        int maxWordsPerCue,
        CaptionAnimation animation,
        bool karaoke)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            throw new DomainException("Caption font family is required.");
        }

        return new CaptionSettings
        {
            Enabled = enabled,
            FontFamily = fontFamily.Trim(),
            FontSizePt = Math.Clamp(fontSizePt, 12, 160),
            PrimaryColor = NormalizeHex(primaryColor, nameof(primaryColor)),
            HighlightColor = NormalizeHex(highlightColor, nameof(highlightColor)),
            OutlineColor = NormalizeHex(outlineColor, nameof(outlineColor)),
            OutlineWidth = Math.Clamp(outlineWidth, 0, 20),
            ShadowDepth = Math.Clamp(shadowDepth, 0, 20),
            Bold = bold,
            Uppercase = uppercase,
            Position = position,
            MarginVerticalPx = Math.Clamp(marginVerticalPx, 0, 900),
            MaxWordsPerCue = Math.Clamp(maxWordsPerCue, 1, 20),
            Animation = animation,
            Karaoke = karaoke
        };
    }

    /// <summary>
    /// Accepts "#RRGGBB" or "RRGGBB" and always stores the "#RRGGBB" form, so
    /// the ASS writer has exactly one shape to convert from.
    /// </summary>
    private static string NormalizeHex(string value, string fieldName)
    {
        var trimmed = (value ?? string.Empty).Trim().TrimStart('#');

        if (trimmed.Length != 6 || !trimmed.All(Uri.IsHexDigit))
        {
            throw new DomainException($"Caption colour '{fieldName}' must be a 6-digit hex colour like #FFCC00.");
        }

        return "#" + trimmed.ToUpperInvariant();
    }
}
