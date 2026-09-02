using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Application.Assets;

public interface IAssetService
{
    Task<IReadOnlyList<AssetResponse>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<AssetResponse> CreateAsync(Guid contentProjectId, CreateAssetRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid contentProjectId, Guid assetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retires the current Ready assets of a given type so a freshly generated
    /// one becomes the only current asset. Called before inserting the
    /// replacement, so a crash in between leaves the scene with no current
    /// asset (visibly "needs regenerating") rather than two rival ones.
    /// </summary>
    /// <param name="sceneId">Null targets project-level assets - the final rendered video, background music.</param>
    Task SupersedeSceneAssetsAsync(Guid contentProjectId, Guid? sceneId, AssetType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a user-uploaded background music track. This replaces the old
    /// "register an asset by typing its file path" form - the wizard must
    /// never ask a user for a server-side path.
    /// </summary>
    Task<AssetResponse> UploadMusicAsync(Guid contentProjectId, string fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a clip the user made themselves (e.g. for free in labs.google/flow)
    /// as this scene's visual, superseding any AI-generated clip or still. This
    /// is the genuinely-$0 path for scenes the user wants full control over.
    /// </summary>
    Task<AssetResponse> UploadSceneVideoAsync(Guid contentProjectId, Guid sceneId, string fileName, Stream content, CancellationToken cancellationToken = default);
}

public class AssetService : IAssetService
{
    private static readonly string[] AllowedMusicExtensions = { ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac" };
    private static readonly string[] AllowedVideoExtensions = { ".mp4", ".mov", ".webm", ".m4v" };

    private readonly IAssetRepository _repository;
    private readonly IFileStorage _fileStorage;

    public AssetService(IAssetRepository repository, IFileStorage fileStorage)
    {
        _repository = repository;
        _fileStorage = fileStorage;
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

    public async Task SupersedeSceneAssetsAsync(Guid contentProjectId, Guid? sceneId, AssetType type, CancellationToken cancellationToken = default)
    {
        var assets = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        var current = assets.Where(a => a.SceneId == sceneId && a.Type == type && a.Status == AssetStatus.Ready).ToList();
        if (current.Count == 0)
        {
            return;
        }

        foreach (var asset in current)
        {
            asset.MarkSuperseded();
        }

        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<AssetResponse> UploadMusicAsync(Guid contentProjectId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedMusicExtensions.Contains(extension))
        {
            throw new DomainException($"Unsupported audio format '{extension}'. Allowed: {string.Join(", ", AllowedMusicExtensions)}.");
        }

        // The uploaded name is never used as a path segment - it comes from the
        // browser and would otherwise be a path-traversal vector.
        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{contentProjectId}/music/track-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}",
            content,
            cancellationToken);

        // Only one background track is mixed in at render time, so retire any
        // earlier upload to keep the project's asset list honest about what
        // will actually be used.
        await SupersedeSceneAssetsAsync(contentProjectId, null, AssetType.Music, cancellationToken);

        return await CreateAsync(
            contentProjectId,
            new CreateAssetRequest(null, AssetType.Music, AssetProviders.Upload, Path.GetFileName(fileName), storedPath, null, null, null),
            cancellationToken);
    }

    public async Task<AssetResponse> UploadSceneVideoAsync(Guid contentProjectId, Guid sceneId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedVideoExtensions.Contains(extension))
        {
            throw new DomainException($"Định dạng video không hỗ trợ '{extension}'. Cho phép: {string.Join(", ", AllowedVideoExtensions)}.");
        }

        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{contentProjectId}/scenes/{sceneId}/upload-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}",
            content,
            cancellationToken);

        // Replace whatever this scene currently uses - AI clip or still.
        await SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Video, cancellationToken);
        await SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Image, cancellationToken);

        return await CreateAsync(
            contentProjectId,
            new CreateAssetRequest(sceneId, AssetType.Video, AssetProviders.Upload, Path.GetFileName(fileName), storedPath, null, null, null),
            cancellationToken);
    }
}
