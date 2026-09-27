using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

public interface IContentProjectRepository
{
    Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ContentProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the project row plus every row this project's own pipeline
    /// produced (script, storyboard/scenes, assets, asset references, QA
    /// scores, generation attempts, AI usage/video-generation cost records).
    /// Deliberately does NOT touch <c>PublishJob</c> rows or any files already
    /// written to storage - publish history (including published links) and
    /// media are preserved even after the source project is gone, since a
    /// still-retryable or in-flight publish job may still need them and a
    /// completed one is a record of what actually got posted. Callers must
    /// verify there's no such in-flight/retryable publish job before calling
    /// this (see ContentProjectService.DeleteAsync).
    /// </summary>
    Task DeleteWithProjectDataAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
