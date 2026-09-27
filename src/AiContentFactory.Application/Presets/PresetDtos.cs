using AiContentFactory.Application.ContentProjects;

namespace AiContentFactory.Application.Presets;

public record ContentTemplateDto(
    string Id,
    string Name,
    string Niche,
    string Description,
    int DefaultDurationSeconds,
    string DefaultAspectRatio,
    string DefaultStylePresetId,
    string DefaultVoicePresetId,
    string DefaultCaptionPresetId)
{
    public static ContentTemplateDto FromCatalog(ContentTemplate template) => new(
        template.Id,
        template.Name,
        template.Niche,
        template.Description,
        template.DefaultDurationSeconds,
        template.DefaultAspectRatio,
        template.DefaultStylePresetId,
        template.DefaultVoicePresetId,
        template.DefaultCaptionPresetId);
}

// ScriptGuidance / VisualStyleGuidance / StyleInstruction are deliberately not
// exposed: they are prompt engineering, and the whole point of the wizard is
// that the user picks "Kinh dị" rather than reading a prompt fragment.
public record StylePresetDto(string Id, string Name, string Description)
{
    public static StylePresetDto FromCatalog(StylePreset preset) => new(preset.Id, preset.Name, preset.Description);
}

public record VoicePresetDto(string Id, string Name, string Description, string Gender, bool IsFree = false)
{
    public static VoicePresetDto FromCatalog(VoicePreset preset) =>
        new(preset.Id, preset.Name, preset.Description, preset.Gender.ToString(), preset.IsFree);
}

public record CaptionPresetDto(string Id, string Name, string Description, CaptionSettingsDto Settings)
{
    public static CaptionPresetDto FromCatalog(CaptionPreset preset) =>
        new(preset.Id, preset.Name, preset.Description, CaptionSettingsDto.FromDomain(preset.Settings));
}

/// <summary>Real per-unit USD cost, so the frontend can show it BEFORE the user spends anything (e.g. reference image generation).</summary>
public record PricingDto(decimal ImageUsd, decimal VideoUsdPerSecond);

public record PresetCatalogResponse(
    IReadOnlyList<ContentTemplateDto> Templates,
    IReadOnlyList<StylePresetDto> Styles,
    IReadOnlyList<VoicePresetDto> Voices,
    IReadOnlyList<CaptionPresetDto> Captions,
    PricingDto Pricing);

/// <summary>
/// Every slot is optional - the wizard's template step sends all four, but
/// changing only the voice later must not reset the caption styling the user
/// already tuned.
/// </summary>
public record ApplyPresetsRequest(
    string? TemplateId,
    string? StylePresetId,
    string? VoicePresetId,
    string? CaptionPresetId);
