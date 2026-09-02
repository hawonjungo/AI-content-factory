using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Presets;

public interface IPresetService
{
    PresetCatalogResponse GetCatalog();

    /// <summary>
    /// Applies preset ids to a project. Choosing a template fills in any slot
    /// the caller left blank with that template's defaults, so the wizard's
    /// first step can be a single click.
    /// </summary>
    Task<ContentProjectResponse?> ApplyAsync(Guid contentProjectId, ApplyPresetsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Caption settings currently in force on the project (preset values plus any manual edits).</summary>
    Task<CaptionSettingsDto?> GetCaptionSettingsAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task<CaptionSettingsDto?> UpdateCaptionSettingsAsync(Guid contentProjectId, CaptionSettingsDto settings, CancellationToken cancellationToken = default);
}

public class PresetService : IPresetService
{
    private readonly IContentProjectRepository _projectRepository;
    private readonly PricingOptions _pricing;

    public PresetService(IContentProjectRepository projectRepository, IOptions<PricingOptions> pricing)
    {
        _projectRepository = projectRepository;
        _pricing = pricing.Value;
    }

    public PresetCatalogResponse GetCatalog() => new(
        PresetCatalog.Templates.Select(ContentTemplateDto.FromCatalog).ToList(),
        PresetCatalog.Styles.Select(StylePresetDto.FromCatalog).ToList(),
        PresetCatalog.Voices.Select(VoicePresetDto.FromCatalog).ToList(),
        PresetCatalog.Captions.Select(CaptionPresetDto.FromCatalog).ToList(),
        new PricingDto(_pricing.ImageUsd, _pricing.VideoUsdPerSecond));

    public async Task<ContentProjectResponse?> ApplyAsync(Guid contentProjectId, ApplyPresetsRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var template = PresetCatalog.FindTemplate(request.TemplateId);
        if (request.TemplateId is not null && template is null)
        {
            throw new DomainException($"Unknown template '{request.TemplateId}'.");
        }

        // A template carries defaults for the other three slots; an explicit
        // choice always wins over them.
        var styleId = request.StylePresetId ?? template?.DefaultStylePresetId;
        var voiceId = request.VoicePresetId ?? template?.DefaultVoicePresetId;
        var captionId = request.CaptionPresetId ?? template?.DefaultCaptionPresetId;

        EnsureExists(styleId, PresetCatalog.FindStyle(styleId) is not null, "style preset");
        EnsureExists(voiceId, PresetCatalog.FindVoice(voiceId) is not null, "voice preset");

        CaptionSettings? captionSettings = null;
        if (captionId is not null)
        {
            var captionPreset = PresetCatalog.FindCaption(captionId)
                ?? throw new DomainException($"Unknown caption preset '{captionId}'.");
            captionSettings = captionPreset.Settings;
        }

        project.ApplyPresets(template?.Id, styleId, voiceId, captionId, captionSettings);
        await _projectRepository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<CaptionSettingsDto?> GetCaptionSettingsAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        return project is null ? null : CaptionSettingsDto.FromDomain(project.Captions);
    }

    public async Task<CaptionSettingsDto?> UpdateCaptionSettingsAsync(Guid contentProjectId, CaptionSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        // ToDomain() validates and clamps, so a hand-rolled request can't put
        // a 900pt font or a malformed colour in front of the renderer.
        project.UpdateCaptions(settings.ToDomain());
        await _projectRepository.SaveChangesAsync(cancellationToken);

        return CaptionSettingsDto.FromDomain(project.Captions);
    }

    private static void EnsureExists(string? id, bool found, string label)
    {
        if (id is not null && !found)
        {
            throw new DomainException($"Unknown {label} '{id}'.");
        }
    }
}
