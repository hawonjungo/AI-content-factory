using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.Generation;

namespace AiContentFactory.Tests.Fakes;

/// <summary>
/// A no-EF stand-in for <see cref="IGenerationAttemptRepository"/>. Keeps rows
/// in a list; <see cref="Now"/> is settable so "today" is deterministic in tests.
/// </summary>
public sealed class InMemoryGenerationAttemptRepository : IGenerationAttemptRepository
{
    public List<GenerationAttempt> Rows { get; } = new();

    public Task AddAsync(GenerationAttempt attempt, CancellationToken cancellationToken = default)
    {
        Rows.Add(attempt);
        return Task.CompletedTask;
    }

    public Task<GenerationAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<GenerationAttempt>> GetSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GenerationAttempt>>(Rows.Where(a => a.CreatedAt >= sinceUtc).ToList());

    public Task<IReadOnlyList<GenerationAttempt>> GetForSceneAsync(Guid contentProjectId, Guid sceneId, GenerationKind kind, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GenerationAttempt>>(
            Rows.Where(a => a.ContentProjectId == contentProjectId && a.SceneId == sceneId && a.Kind == kind)
                .OrderBy(a => a.CreatedAt)
                .ToList());

    public Task<IReadOnlyList<GenerationAttempt>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GenerationAttempt>>(
            Rows.Where(a => a.ContentProjectId == contentProjectId)
                .OrderByDescending(a => a.CreatedAt)
                .ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
