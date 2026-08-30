using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

public record CreateContentProjectRequest(
    string Title,
    string? Topic,
    string? Niche,
    int TargetDurationSeconds,
    string? AspectRatio,
    string? Language);

public record UpdateContentProjectRequest(
    string Title,
    string? Topic,
    string? Niche,
    int TargetDurationSeconds);

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
        project.CreatedAt,
        project.UpdatedAt);
}
