using AiContentFactory.Domain.Assets;

namespace AiContentFactory.Application.Assets;

public interface IAssetRepository
{
    Task<IReadOnlyList<Asset>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<Asset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Asset asset, CancellationToken cancellationToken = default);
    void Remove(Asset asset);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
