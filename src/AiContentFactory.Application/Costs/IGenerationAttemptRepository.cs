using AiContentFactory.Domain.Generation;

namespace AiContentFactory.Application.Costs;

public interface IGenerationAttemptRepository
{
    Task AddAsync(GenerationAttempt attempt, CancellationToken cancellationToken = default);

    Task<GenerationAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every attempt created on or after <paramref name="sinceUtc"/>, account-wide.
    /// The credit ledger sums these to work out how much of today's budget is
    /// spoken for.
    /// </summary>
    Task<IReadOnlyList<GenerationAttempt>> GetSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>All attempts for one scene + kind, oldest first - used to count retries.</summary>
    Task<IReadOnlyList<GenerationAttempt>> GetForSceneAsync(Guid contentProjectId, Guid sceneId, GenerationKind kind, CancellationToken cancellationToken = default);

    /// <summary>Every attempt for a project, newest first - for the generation-progress UI.</summary>
    Task<IReadOnlyList<GenerationAttempt>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
