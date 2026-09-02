using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Domain.AssetReferences;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class AssetReferenceRepository : IAssetReferenceRepository
{
    private readonly AppDbContext _db;

    public AssetReferenceRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AssetReference>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        await _db.AssetReferences
            .AsNoTracking()
            .Where(r => r.ContentProjectId == contentProjectId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<AssetReference?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.AssetReferences.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<AssetReference> references, CancellationToken cancellationToken = default) =>
        await _db.AssetReferences.AddRangeAsync(references, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);

    public async Task<int> ApproveAtomicAsync(Guid contentProjectId, Guid refId, AssetReferenceType type, CancellationToken cancellationToken = default)
    {
        // Two direct UPDATEs, no tracked entities: rapid re-clicks or an
        // approve+skip race just no-op instead of raising DbUpdateConcurrencyException.
        await _db.AssetReferences
            .Where(r => r.ContentProjectId == contentProjectId
                && r.Type == type
                && r.Id != refId
                && (r.Status == AssetReferenceStatus.Generated
                    || r.Status == AssetReferenceStatus.Approved
                    || r.Status == AssetReferenceStatus.Skipped
                    || r.Status == AssetReferenceStatus.Pending))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AssetReferenceStatus.Superseded)
                .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);

        return await _db.AssetReferences
            .Where(r => r.Id == refId && r.ContentProjectId == contentProjectId && r.ImagePath != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AssetReferenceStatus.Approved)
                .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task SkipAtomicAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default)
    {
        await _db.AssetReferences
            .Where(r => r.ContentProjectId == contentProjectId && r.Type == type)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, AssetReferenceStatus.Superseded)
                .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);

        _db.AssetReferences.Add(AssetReference.CreateSkipped(contentProjectId, type));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task ClearVariantsAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default) =>
        _db.AssetReferences
            .Where(r => r.ContentProjectId == contentProjectId
                && r.Type == type
                && r.Status != AssetReferenceStatus.Approved
                && r.Status != AssetReferenceStatus.Skipped)
            .ExecuteDeleteAsync(cancellationToken);

    public Task DeleteAsync(Guid refId, CancellationToken cancellationToken = default) =>
        _db.AssetReferences.Where(r => r.Id == refId).ExecuteDeleteAsync(cancellationToken);
}
