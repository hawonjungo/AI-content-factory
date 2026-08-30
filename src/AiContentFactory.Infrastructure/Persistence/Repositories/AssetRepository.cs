using AiContentFactory.Application.Assets;
using AiContentFactory.Domain.Assets;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class AssetRepository : IAssetRepository
{
    private readonly AppDbContext _db;

    public AssetRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Asset>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        await _db.Assets
            .Where(a => a.ContentProjectId == contentProjectId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Asset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task AddAsync(Asset asset, CancellationToken cancellationToken = default) =>
        await _db.Assets.AddAsync(asset, cancellationToken);

    public void Remove(Asset asset) => _db.Assets.Remove(asset);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
