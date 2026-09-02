using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

/// <summary>
/// The wizard picks a template first, so preset ids arrive with the very first
/// request rather than as a follow-up call. All four are optional so the
/// original "just create a project" flow (and the Advanced page) still works.
/// </summary>
public record CreateContentProjectRequest(
    string Title,
    string? Topic,
    string? Niche,
    int TargetDurationSeconds,
    string? AspectRatio,
    string? Language,
    string? TemplateId = null,
    string? StylePresetId = null,
    string? VoicePresetId = null,
    string? CaptionPresetId = null,
    ContentIdeaConfigDto? IdeaConfig = null);

public record UpdateContentProjectRequest(
    string Title,
    string? Topic,
    string? Niche,
    int TargetDurationSeconds,
    ContentIdeaConfigDto? IdeaConfig = null);

public record ChangeContentProjectStatusRequest(ContentProjectStatus Status);

public record ContentProjectResponse(
    Guid Id,
    string Title,
    string? Topic,
    string? Niche,
    string Status,
    int TargetDurationSeconds,
    string AspectRatio,
    string Language,
    string? TemplateId,
    string? StylePresetId,
    string? VoicePresetId,
    string? CaptionPresetId,
    CaptionSettingsDto Captions,
    ContentIdeaConfigDto IdeaConfig,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ContentProjectResponse FromDomain(ContentProject project) => new(
        project.Id,
        project.Title,
        project.Topic,
        project.Niche,
        project.Status.ToString(),
        project.TargetDurationSeconds,
        project.AspectRatio,
        project.Language,
        project.TemplateId,
        project.StylePresetId,
        project.VoicePresetId,
        project.CaptionPresetId,
        CaptionSettingsDto.FromDomain(project.Captions),
        ContentIdeaConfigDto.FromDomain(project.IdeaConfig),
        project.CreatedAt,
        project.UpdatedAt);
}
