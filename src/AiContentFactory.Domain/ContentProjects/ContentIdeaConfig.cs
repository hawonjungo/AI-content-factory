namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// The Step 2 "idea configuration" - everything the user tells the pipeline
/// about the video beyond title/topic/niche. Persisted as a single jsonb column
/// on <see cref="ContentProject"/> (same pattern as CaptionSettings): it is read
/// and written whole, never queried by field.
///
/// The narrative fields (pillar, audience, story type, hook style, emotion) are
/// folded into the script prompt. The voice fields drive the narration
/// VoiceProfile. <see cref="CreditStrategy"/> steers storyboard video allocation.
/// </summary>
public class ContentIdeaConfig
{
    public string? ContentPillar { get; private set; }
    public string? TargetAudience { get; private set; }
    public string? StoryType { get; private set; }
    public string? HookStyle { get; private set; }
    public string? Emotion { get; private set; }

    public VoiceGender VoiceGender { get; private set; } = VoiceGender.Unspecified;

    /// <summary>Natural-language delivery direction ("calm", "energetic", "dramatic"); null = use the voice preset's own.</summary>
    public string? VoiceStyle { get; private set; }

    /// <summary>Relative speaking rate, 1.0 = normal. Null = provider default. Clamped 0.5-1.5 here (the useful range).</summary>
    public double? SpeakingRate { get; private set; }

    /// <summary>BCP-47 language/accent hint ("vi-VN", "en-US"); null lets the provider infer.</summary>
    public string? NarrationLanguage { get; private set; }

    public CreditStrategy CreditStrategy { get; private set; } = CreditStrategy.Balanced;

    private ContentIdeaConfig()
    {
        // EF Core / JSON
    }

    public static ContentIdeaConfig Default() => new();

    public static ContentIdeaConfig Create(
        string? contentPillar,
        string? targetAudience,
        string? storyType,
        string? hookStyle,
        string? emotion,
        VoiceGender voiceGender,
        string? voiceStyle,
        double? speakingRate,
        string? narrationLanguage,
        CreditStrategy creditStrategy) => new()
    {
        ContentPillar = Trim(contentPillar),
        TargetAudience = Trim(targetAudience),
        StoryType = Trim(storyType),
        HookStyle = Trim(hookStyle),
        Emotion = Trim(emotion),
        VoiceGender = voiceGender,
        VoiceStyle = Trim(voiceStyle),
        SpeakingRate = speakingRate is { } r ? Math.Clamp(r, 0.5, 1.5) : null,
        NarrationLanguage = Trim(narrationLanguage),
        CreditStrategy = creditStrategy
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
