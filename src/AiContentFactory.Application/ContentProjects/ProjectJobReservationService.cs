using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.ContentProjects;

/// <summary>
/// The shared "busy-lock" reservation every controller performs before
/// enqueueing a long-running background job for a project: mark the
/// project's <see cref="GenerationProgress"/> busy (stage + message) and
/// persist it BEFORE the job reaches Hangfire, so duplicate clicks/API
/// requests are rejected while the job is still only queued. Extracted out
/// of <c>ContentProjectsController</c> (originally a private
/// <c>ReserveJobAsync</c> helper) so any controller that needs to
/// reserve-then-enqueue - e.g. <c>StoryboardsController</c> auto-chaining the
/// bulk prompt-suggestion job after "Chia clip" - can reuse the exact same
/// logic instead of re-implementing it.
/// </summary>
public interface IProjectJobReservationService
{
    /// <summary>
    /// Returns the project with its progress marked busy, or null if the
    /// project doesn't exist or is already busy with another job (the
    /// caller should treat null as "reservation failed" - either 404 the
    /// project, or reject/skip the job it was about to enqueue).
    /// </summary>
    Task<ContentProject?> ReserveAsync(Guid contentProjectId, string stage, string message, CancellationToken cancellationToken = default);
}

public class ProjectJobReservationService : IProjectJobReservationService
{
    private readonly IContentProjectRepository _projectRepository;

    public ProjectJobReservationService(IContentProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    public async Task<ContentProject?> ReserveAsync(Guid contentProjectId, string stage, string message, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        if (project is null || !project.Progress.IsIdle) return null;

        // Persist this before Hangfire receives the job. The UI sees `busy`
        // immediately, and duplicate clicks/API requests are rejected while
        // the job is still waiting in Hangfire's queue.
        project.ReportProgress(stage, 0, 1, message);
        await _projectRepository.SaveChangesAsync(cancellationToken);
        return project;
    }
}
