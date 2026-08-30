namespace AiContentFactory.Application.ContentProjects;

public interface IContentProjectService
{
    Task<ContentProjectResponse> CreateAsync(CreateContentProjectRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentProjectResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> UpdateAsync(Guid id, UpdateContentProjectRequest request, CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> ChangeStatusAsync(Guid id, ChangeContentProjectStatusRequest request, CancellationToken cancellationToken = default);
}
