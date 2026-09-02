using AiContentFactory.Application.Presets;
using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

public class ContentProjectService : IContentProjectService
{
    private readonly IContentProjectRepository _repository;

    public ContentProjectService(IContentProjectRepository repository)
    {
        _repository = repository;
    }

    public async Task<ContentProjectResponse> CreateAsync(CreateContentProjectRequest request, CancellationToken cancellationToken = default)
    {
        var template = PresetCatalog.FindTemplate(request.TemplateId);

        var project = ContentProject.Create(
            request.Title,
            request.Topic,
            // A template's niche is a sensible default but never overrides one
            // the user typed.
            request.Niche ?? template?.Niche,
            request.TargetDurationSeconds,
            request.AspectRatio ?? template?.DefaultAspectRatio ?? "9:16",
            request.Language ?? "en");

        if (template is not null || request.StylePresetId is not null || request.VoicePresetId is not null || request.CaptionPresetId is not null)
        {
            var captionPresetId = request.CaptionPresetId ?? template?.DefaultCaptionPresetId;

            project.ApplyPresets(
                template?.Id,
                request.StylePresetId ?? template?.DefaultStylePresetId,
                request.VoicePresetId ?? template?.DefaultVoicePresetId,
                captionPresetId,
                PresetCatalog.FindCaption(captionPresetId)?.Settings);
        }

        if (request.IdeaConfig is not null)
        {
            project.UpdateIdeaConfig(request.IdeaConfig.ToDomain());
        }

        await _repository.AddAsync(project, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<IReadOnlyList<ContentProjectResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _repository.GetAllAsync(cancellationToken);
        return projects.Select(ContentProjectResponse.FromDomain).ToList();
    }

    public async Task<ContentProjectResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        return project is null ? null : ContentProjectResponse.FromDomain(project);
    }

    public async Task<ContentProjectResponse?> UpdateAsync(Guid id, UpdateContentProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.UpdateDetails(request.Title, request.Topic, request.Niche, request.TargetDurationSeconds);
        if (request.IdeaConfig is not null)
        {
            project.UpdateIdeaConfig(request.IdeaConfig.ToDomain());
        }
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<ContentProjectResponse?> ChangeStatusAsync(Guid id, ChangeContentProjectStatusRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.TransitionTo(request.Status);
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }
}
