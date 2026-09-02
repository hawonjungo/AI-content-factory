using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.Generation;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class GenerationAttemptRepository : IGenerationAttemptRepository
{
    private readonly AppDbContext _db;

    public GenerationAttemptRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(GenerationAttempt attempt, CancellationToken cancellationToken = default) =>
        await _db.Set<GenerationAttempt>().AddAsync(attempt, cancellationToken);

    public Task<GenerationAttempt?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Set<GenerationAttempt>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<GenerationAttempt>> GetSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default) =>
        await _db.Set<GenerationAttempt>()
            .AsNoTracking()
            .Where(a => a.CreatedAt >= sinceUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GenerationAttempt>> GetForSceneAsync(
        Guid contentProjectId,
        Guid sceneId,
        GenerationKind kind,
        CancellationToken cancellationToken = default) =>
        await _db.Set<GenerationAttempt>()
            .AsNoTracking()
            .Where(a => a.ContentProjectId == contentProjectId && a.SceneId == sceneId && a.Kind == kind)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GenerationAttempt>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        await _db.Set<GenerationAttempt>()
            .AsNoTracking()
            .Where(a => a.ContentProjectId == contentProjectId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
