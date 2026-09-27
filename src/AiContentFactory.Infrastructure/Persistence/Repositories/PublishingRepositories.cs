using AiContentFactory.Application.Publishing;
using AiContentFactory.Domain.Publishing;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class PublishJobRepository : IPublishJobRepository
{
    private readonly AppDbContext _db;

    public PublishJobRepository(AppDbContext db) => _db = db;

    public Task<PublishJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Set<PublishJob>().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PublishJob>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        await _db.Set<PublishJob>()
            .Where(j => j.ContentProjectId == contentProjectId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<PublishJob?> GetActiveAsync(Guid contentProjectId, PublishTarget platform, CancellationToken cancellationToken = default) =>
        _db.Set<PublishJob>()
            .Where(j => j.ContentProjectId == contentProjectId && j.Platform == platform)
            .Where(j => j.Status == PublishJobStatus.Pending
                || j.Status == PublishJobStatus.Scheduled
                || j.Status == PublishJobStatus.Publishing
                || j.Status == PublishJobStatus.Published)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) =>
        await _db.Set<PublishJob>().AddAsync(job, cancellationToken);

    public Task RemoveRangeAsync(IEnumerable<PublishJob> jobs, CancellationToken cancellationToken = default)
    {
        _db.Set<PublishJob>().RemoveRange(jobs);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}

public class SocialConnectionRepository : ISocialConnectionRepository
{
    private readonly AppDbContext _db;

    public SocialConnectionRepository(AppDbContext db) => _db = db;

    public Task<SocialConnection?> GetAsync(PublishTarget platform, CancellationToken cancellationToken = default) =>
        _db.Set<SocialConnection>().FirstOrDefaultAsync(c => c.Platform == platform, cancellationToken);

    public async Task<IReadOnlyList<SocialConnection>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Set<SocialConnection>().ToListAsync(cancellationToken);

    public async Task AddAsync(SocialConnection connection, CancellationToken cancellationToken = default) =>
        await _db.Set<SocialConnection>().AddAsync(connection, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
