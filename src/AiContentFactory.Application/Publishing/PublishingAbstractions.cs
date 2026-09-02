using AiContentFactory.Domain.Publishing;

namespace AiContentFactory.Application.Publishing;

/// <summary>Encrypts OAuth tokens at rest. Implemented in Infrastructure (AES); tokens never reach the API surface or the frontend.</summary>
public interface ITokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

/// <summary>
/// Hands a publish job to the background system. Kept as a one-method seam so
/// the Application layer never references Hangfire directly - the Infrastructure
/// implementation is the only thing that knows the scheduler.
/// </summary>
public interface IPublishJobScheduler
{
    /// <summary>Run the job as soon as a worker is free.</summary>
    void EnqueueNow(Guid publishJobId);

    /// <summary>Run the job at (or shortly after) the given UTC instant.</summary>
    void Schedule(Guid publishJobId, DateTimeOffset runAtUtc);
}

public interface IPublishJobRepository
{
    Task<PublishJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublishJob>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<PublishJob?> GetActiveAsync(Guid contentProjectId, PublishTarget platform, CancellationToken cancellationToken = default);
    Task AddAsync(PublishJob job, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ISocialConnectionRepository
{
    Task<SocialConnection?> GetAsync(PublishTarget platform, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SocialConnection>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(SocialConnection connection, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
