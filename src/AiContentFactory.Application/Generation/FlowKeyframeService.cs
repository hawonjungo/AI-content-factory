using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// The FREE half of the two-step Google Flow workflow (first frame, then
/// video): the scene's first frame comes either from an image the user made in
/// Flow and uploads here, or from the last frame of the previous scene's clip
/// (local FFmpeg, for shot-to-shot continuity). Neither path calls an AI
/// provider or spends anything. The frame is stored as the scene's Keyframe
/// (same Asset + <see cref="Scene.KeyframeAssetId"/> slot the paid in-app
/// Keyframe uses) and approved straight away - the user picked it.
/// </summary>
public interface IFlowKeyframeService
{
    Task<SceneResponse> UploadFirstFrameAsync(Guid contentProjectId, Guid sceneId, string? fileName, Stream content, CancellationToken cancellationToken = default);

    Task<SceneResponse> UsePreviousClipLastFrameAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);
}

public class FlowKeyframeService : IFlowKeyframeService
{
    private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg" };
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    private const int MaxDimensionPx = 8192;

    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IAssetService _assetService;
    private readonly IFileStorage _fileStorage;
    private readonly IMediaProbe _mediaProbe;
    private readonly IVideoFrameExtractor _frameExtractor;
    private readonly ILogger<FlowKeyframeService> _logger;

    public FlowKeyframeService(
        IStoryboardRepository storyboardRepository,
        IAssetService assetService,
        IFileStorage fileStorage,
        IMediaProbe mediaProbe,
        IVideoFrameExtractor frameExtractor,
        ILogger<FlowKeyframeService> logger)
    {
        _storyboardRepository = storyboardRepository;
        _assetService = assetService;
        _fileStorage = fileStorage;
        _mediaProbe = mediaProbe;
        _frameExtractor = frameExtractor;
        _logger = logger;
    }

    public async Task<SceneResponse> UploadFirstFrameAsync(Guid contentProjectId, Guid sceneId, string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        // The client's file name only feeds the extension allow-list; the stored extension follows the real bytes.
        var clientExtension = string.IsNullOrWhiteSpace(fileName) ? string.Empty : Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(clientExtension))
        {
            throw new DomainException($"Định dạng ảnh không hỗ trợ '{clientExtension}'. Cho phép: {string.Join(", ", AllowedExtensions)}.");
        }

        var (storyboard, scene) = await LoadSceneAsync(contentProjectId, sceneId, cancellationToken);
        var bytes = await UploadedImage.ReadCappedAsync(content, cancellationToken);
        var extension = UploadedImage.SniffExtension(bytes)
            ?? throw new DomainException("File không phải ảnh PNG hoặc JPEG hợp lệ.");

        await StoreAsKeyframeAsync(contentProjectId, scene, bytes, extension, "flow-upload", "Uploaded first frame (made in Google Flow)", cancellationToken);
        _logger.LogInformation("First frame uploaded for Scene {SceneId} (ContentProject {ContentProjectId})", sceneId, contentProjectId);
        return await FreshSceneAsync(contentProjectId, sceneId, cancellationToken);
    }

    public async Task<SceneResponse> UsePreviousClipLastFrameAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        var (storyboard, scene) = await LoadSceneAsync(contentProjectId, sceneId, cancellationToken);

        var previous = storyboard.Scenes
            .Where(s => s.SceneNumber < scene.SceneNumber)
            .OrderByDescending(s => s.SceneNumber)
            .FirstOrDefault()
            ?? throw new DomainException("Đây là cảnh đầu tiên - không có cảnh trước để lấy khung cuối.");

        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        var previousClip = assets
            .Where(a => a.SceneId == previous.Id
                && a.Type == nameof(AssetType.Video)
                && a.Status == nameof(AssetStatus.Ready)
                && !string.IsNullOrWhiteSpace(a.FilePath))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault()
            ?? throw new DomainException($"Cảnh {previous.SceneNumber} chưa có clip video - hãy nhập clip cho cảnh đó trước.");

        byte[] frame;
        try
        {
            frame = await _frameExtractor.ExtractFrameAsync(_fileStorage.GetAbsolutePath(previousClip.FilePath!), atSeconds: null, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Last-frame extraction failed for Scene {SceneId}'s previous clip {AssetId}", sceneId, previousClip.Id);
            throw new DomainException($"Không lấy được khung cuối của clip cảnh {previous.SceneNumber}.");
        }

        await StoreAsKeyframeAsync(
            contentProjectId, scene, frame, ".png", "last-frame",
            $"Last frame of scene {previous.SceneNumber}'s clip", cancellationToken);
        _logger.LogInformation("Scene {SceneId} continues from the last frame of scene {PreviousScene}", sceneId, previous.SceneNumber);
        return await FreshSceneAsync(contentProjectId, sceneId, cancellationToken);
    }

    private async Task<(Storyboard Storyboard, Scene Scene)> LoadSceneAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken)
    {
        var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        // Storing a first frame retires the scene's current visual (see below),
        // which would silently drop a clip the user already imported.
        if (scene.SkipGeneration)
        {
            throw new DomainException("Cảnh này đã có clip video. Bỏ chọn \"Đã có video\" trước nếu muốn làm lại từ ảnh khung đầu.");
        }

        if (scene.Status == SceneStatus.Generating || scene.KeyframeStatus == KeyframeStatus.Generating)
        {
            throw new DomainException("Cảnh này đang được tạo - vui lòng đợi xong rồi thử lại.");
        }

        return (storyboard, scene);
    }

    private async Task StoreAsKeyframeAsync(
        Guid contentProjectId, Scene scene, byte[] bytes, string extension, string provider, string description, CancellationToken cancellationToken)
    {
        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{contentProjectId}/scenes/{scene.Id}/keyframe-{provider}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}",
            bytes,
            cancellationToken);

        try
        {
            await ValidateStoredImageAsync(storedPath, cancellationToken);
        }
        catch
        {
            await TryDeleteAsync(storedPath);
            throw;
        }

        // Same rule as the in-app Keyframe (SceneKeyframeService): a scene has
        // one current visual, so both older kinds are retired - the frame then
        // stands in as a still until the Flow clip is imported over it.
        await _assetService.SupersedeSceneAssetsAsync(contentProjectId, scene.Id, AssetType.Video, cancellationToken);
        await _assetService.SupersedeSceneAssetsAsync(contentProjectId, scene.Id, AssetType.Image, cancellationToken);
        var asset = await _assetService.CreateAsync(
            contentProjectId,
            new CreateAssetRequest(scene.Id, AssetType.Image, provider, description, storedPath, null, null, null),
            cancellationToken);

        scene.MarkKeyframeGenerated(asset.Id, description);
        scene.ApproveKeyframe();
        await _storyboardRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateStoredImageAsync(string storedPath, CancellationToken cancellationToken)
    {
        MediaInfo media;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                media = await _mediaProbe.ProbeAsync(_fileStorage.GetAbsolutePath(storedPath), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new DomainException("Không đọc được file ảnh (quá thời gian xử lý). Hãy thử một file ảnh khác.");
            }
        }

        if (!media.Ok || !media.HasVideo || media.Width <= 0 || media.Height <= 0)
        {
            _logger.LogWarning("Uploaded first frame is not a readable image: {ProbeError}", media.Error);
            throw new DomainException("Không đọc được file ảnh. Hãy thử một file PNG hoặc JPEG khác.");
        }

        if (media.Width > MaxDimensionPx || media.Height > MaxDimensionPx)
        {
            throw new DomainException($"Ảnh quá lớn (tối đa {MaxDimensionPx}px mỗi cạnh).");
        }
    }

    private async Task TryDeleteAsync(string storedPath)
    {
        try
        {
            await _fileStorage.DeleteAsync(storedPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete rejected first-frame upload {Path}", storedPath);
        }
    }

    private async Task<SceneResponse> FreshSceneAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken)
    {
        var fresh = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after storing the first frame.");
        return StoryboardResponse.FromDomain(fresh).Scenes.First(s => s.Id == sceneId);
    }
}
