using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

public interface IContentProjectRepository
{
    Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ContentProject project, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
