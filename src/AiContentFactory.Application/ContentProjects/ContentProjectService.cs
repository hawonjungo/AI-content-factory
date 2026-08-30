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
        var project = ContentProject.Create(
            request.Title,
            request.Topic,
            request.Niche,
            request.TargetDurationSeconds,
            request.AspectRatio ?? "9:16",
            request.Language ?? "en");

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
