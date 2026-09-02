using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Tests.Fakes;

/// <summary>
/// Minimal <see cref="IStoryboardService"/> over a shared <see cref="FakeStoryboardRepository"/>.
/// Only the members the Flow-import / audio-timing paths use are implemented.
/// </summary>
public sealed class FakeStoryboardService : IStoryboardService
{
    private readonly FakeStoryboardRepository _repo;

    public FakeStoryboardService(FakeStoryboardRepository repo) => _repo = repo;

    public Task<StoryboardResponse> GetOrCreateAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var sb = _repo.Current ?? Storyboard.Create(contentProjectId);
        return Task.FromResult(StoryboardResponse.FromDomain(sb));
    }

    public Task SetSceneAudioTimingAsync(Guid contentProjectId, Guid sceneId, string? audioTimingJson, CancellationToken cancellationToken = default)
    {
        var scene = _repo.Current?.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException("scene not found");
        scene.SetAudioTiming(audioTimingJson);
        return Task.CompletedTask;
    }

    public Task<StoryboardResponse> AddSceneAsync(Guid c, CreateSceneRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<StoryboardResponse> UpdateSceneAsync(Guid c, Guid s, UpdateSceneRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<StoryboardResponse> RemoveSceneAsync(Guid c, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SetScenePromptAsync(Guid c, Guid s, string p, string? n, string? v, string pr, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<StoryboardResponse> SetSceneVisualTypeAsync(Guid c, Guid s, SceneVisualType v, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<StoryboardResponse> SetSceneModelTierAsync(Guid c, Guid sceneId, string? modelTier, CancellationToken ct = default)
    {
        var scene = _repo.Current?.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException("scene not found");
        scene.SetModelTier(modelTier);
        return Task.FromResult(StoryboardResponse.FromDomain(_repo.Current!));
    }

    public Task<StoryboardResponse> SetSceneCameraMovementAsync(Guid c, Guid sceneId, CameraMovement cameraMovement, CancellationToken ct = default)
    {
        var scene = _repo.Current?.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException("scene not found");
        scene.SetCameraMovement(cameraMovement);
        return Task.FromResult(StoryboardResponse.FromDomain(_repo.Current!));
    }
    public Task<StoryboardResponse> SetScenePromptTextAsync(Guid c, Guid s, string? p, string? n, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<StoryboardResponse> SuggestScenePromptAsync(Guid c, Guid s, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>In-memory <see cref="IAssetService"/>. Reads/creates/supersedes; uploads throw.</summary>
public sealed class FakeAssetService : IAssetService
{
    private readonly List<AssetResponse> _assets = new();

    public IReadOnlyList<AssetResponse> All => _assets;

    public Task<IReadOnlyList<AssetResponse>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssetResponse>>(_assets.Where(a => a.ContentProjectId == contentProjectId).ToList());

    public Task<AssetResponse> CreateAsync(Guid contentProjectId, CreateAssetRequest request, CancellationToken cancellationToken = default)
    {
        var status = string.IsNullOrWhiteSpace(request.FilePath) ? nameof(AssetStatus.Pending) : nameof(AssetStatus.Ready);
        var asset = new AssetResponse(
            Guid.NewGuid(), contentProjectId, request.SceneId, request.Type.ToString(), request.FilePath,
            request.Provider, request.Prompt, request.DurationSeconds, request.Width, request.Height,
            status, DateTimeOffset.UtcNow);
        _assets.Add(asset);
        return Task.FromResult(asset);
    }

    public Task DeleteAsync(Guid contentProjectId, Guid assetId, CancellationToken cancellationToken = default)
    {
        _assets.RemoveAll(a => a.Id == assetId);
        return Task.CompletedTask;
    }

    public Task SupersedeSceneAssetsAsync(Guid contentProjectId, Guid? sceneId, AssetType type, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < _assets.Count; i++)
        {
            var a = _assets[i];
            if (a.ContentProjectId == contentProjectId && a.SceneId == sceneId && a.Type == type.ToString() && a.Status == nameof(AssetStatus.Ready))
            {
                _assets[i] = a with { Status = nameof(AssetStatus.Superseded) };
            }
        }

        return Task.CompletedTask;
    }

    public Task<AssetResponse> UploadMusicAsync(Guid c, string f, Stream s, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AssetResponse> UploadSceneVideoAsync(Guid c, Guid s, string f, Stream st, CancellationToken ct = default) => throw new NotSupportedException();
}
