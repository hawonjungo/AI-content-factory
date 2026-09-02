using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

/// <summary>
/// Wire shape for <see cref="CaptionSettings"/>. Shared by the preset catalog
/// (so the UI can preview a preset before applying it) and the per-project
/// caption editor, so both sides speak exactly one vocabulary.
/// </summary>
public record CaptionSettingsDto(
    bool Enabled,
    string FontFamily,
    int FontSizePt,
    string PrimaryColor,
    string HighlightColor,
    string OutlineColor,
    double OutlineWidth,
    double ShadowDepth,
    bool Bold,
    bool Uppercase,
    string Position,
    int MarginVerticalPx,
    int MaxWordsPerCue,
    string Animation,
    bool Karaoke)
{
    public static CaptionSettingsDto FromDomain(CaptionSettings settings) => new(
        settings.Enabled,
        settings.FontFamily,
        settings.FontSizePt,
        settings.PrimaryColor,
        settings.HighlightColor,
        settings.OutlineColor,
        settings.OutlineWidth,
        settings.ShadowDepth,
        settings.Bold,
        settings.Uppercase,
        settings.Position.ToString(),
        settings.MarginVerticalPx,
        settings.MaxWordsPerCue,
        settings.Animation.ToString(),
        settings.Karaoke);

    public CaptionSettings ToDomain() => CaptionSettings.Create(
        Enabled,
        FontFamily,
        FontSizePt,
        PrimaryColor,
        HighlightColor,
        OutlineColor,
        OutlineWidth,
        ShadowDepth,
        Bold,
        Uppercase,
        ParseEnum(Position, CaptionPosition.Bottom),
        MarginVerticalPx,
        MaxWordsPerCue,
        ParseEnum(Animation, CaptionAnimation.PopIn),
        Karaoke);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
