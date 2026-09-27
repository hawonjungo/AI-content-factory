using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class ContentProjectRepository : IContentProjectRepository
{
    private readonly AppDbContext _db;

    public ContentProjectRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.ContentProjects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.ContentProjects
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ContentProject project, CancellationToken cancellationToken = default) =>
        await _db.ContentProjects.AddAsync(project, cancellationToken);

    public async Task DeleteWithProjectDataAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        // Bulk SQL deletes (no entities loaded into the change tracker) - fine
        // here since nothing about this operation needs domain behaviour, just
        // "every row that belongs to this project, in these tables, is gone".
        // Storyboard -> Scene has a real DB-level cascade FK, so deleting
        // Storyboards here also removes their Scenes. ExecuteDeleteAsync runs
        // immediately per call rather than batching through SaveChanges, so
        // this needs its own transaction to stay all-or-nothing.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.Scripts.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.Storyboards.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.Assets.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.AssetReferences.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.QaScores.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.GenerationAttempts.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.AiUsageRecords.Where(x => x.ContentProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.VideoGenerationRecords.Where(x => x.ProjectId == contentProjectId).ExecuteDeleteAsync(cancellationToken);
        await _db.ContentProjects.Where(x => x.Id == contentProjectId).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
