using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Application.Assets;

public interface IAssetService
{
    Task<IReadOnlyList<AssetResponse>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<AssetResponse> CreateAsync(Guid contentProjectId, CreateAssetRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid contentProjectId, Guid assetId, CancellationToken cancellationToken = default);
}

public class AssetService : IAssetService
{
    private readonly IAssetRepository _repository;

    public AssetService(IAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<AssetResponse>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var assets = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        return assets.Select(AssetResponse.FromDomain).ToList();
    }

    public async Task<AssetResponse> CreateAsync(Guid contentProjectId, CreateAssetRequest request, CancellationToken cancellationToken = default)
    {
        var asset = Asset.CreatePending(contentProjectId, request.SceneId, request.Type, request.Provider, request.Prompt);

        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            asset.MarkReady(request.FilePath, request.DurationSeconds, request.Width, request.Height);
        }

        await _repository.AddAsync(asset, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return AssetResponse.FromDomain(asset);
    }

    public async Task DeleteAsync(Guid contentProjectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _repository.GetByIdAsync(assetId, cancellationToken);
        if (asset is null || asset.ContentProjectId != contentProjectId)
        {
            throw new DomainException($"Asset '{assetId}' was not found on this content project.");
        }

        _repository.Remove(asset);
        await _repository.SaveChangesAsync(cancellationToken);
    }
}
