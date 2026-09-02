using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

/// <summary>
/// Wire form of <see cref="ContentIdeaConfig"/>. Enums travel as strings
/// (the API serializes enums as strings project-wide); unknown/blank values
/// fall back to the safe default.
/// </summary>
public record ContentIdeaConfigDto(
    string? ContentPillar,
    string? TargetAudience,
    string? StoryType,
    string? HookStyle,
    string? Emotion,
    string? VoiceGender,
    string? VoiceStyle,
    double? SpeakingRate,
    string? NarrationLanguage,
    string? CreditStrategy)
{
    public static ContentIdeaConfigDto FromDomain(ContentIdeaConfig config) => new(
        config.ContentPillar,
        config.TargetAudience,
        config.StoryType,
        config.HookStyle,
        config.Emotion,
        config.VoiceGender.ToString(),
        config.VoiceStyle,
        config.SpeakingRate,
        config.NarrationLanguage,
        config.CreditStrategy.ToString());

    public ContentIdeaConfig ToDomain() => ContentIdeaConfig.Create(
        ContentPillar,
        TargetAudience,
        StoryType,
        HookStyle,
        Emotion,
        ParseEnum(VoiceGender, Domain.ContentProjects.VoiceGender.Unspecified),
        VoiceStyle,
        SpeakingRate,
        NarrationLanguage,
        ParseEnum(CreditStrategy, Domain.ContentProjects.CreditStrategy.Balanced));

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
