using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <param name="Accepted">False when the clip failed a hard check - nothing was stored and the scene is unchanged.</param>
/// <param name="AspectLabel">The clip's aspect ratio, e.g. "9:16", "16:9".</param>
public record FlowClipImportResult(
    Guid SceneId,
    int SceneNumber,
    bool Accepted,
    double DurationSeconds,
    int Width,
    int Height,
    string AspectLabel,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Warnings);

public record FlowImportSceneStatus(
    Guid SceneId,
    int SceneNumber,
    string GenerationType,
    bool NeedsFlowClip,
    bool HasClip,
    bool ClipValid,
    double? DurationSeconds,
    int? Width,
    int? Height,
    IReadOnlyList<string> Issues);

/// <param name="ReadyForRender">Every scene has a usable visual (Flow clip, AI still, or upload).</param>
public record FlowImportStatus(
    int TotalScenes,
    int ScenesNeedingFlow,
    int ImportedValid,
    int MissingOrInvalid,
    bool ReadyForRender,
    IReadOnlyList<FlowImportSceneStatus> Scenes);

/// <summary>
/// The post-Flow import step: the user drops in the clips they generated in
/// Google Flow, and the app validates each (readable, non-zero duration,
/// resolution/aspect), matches it to its scene, records the metadata, marks the
/// scene complete, and books the credits it cost so the ledger stays honest.
/// It also reports which scenes are still missing or invalid so the timeline is
/// never built with a hole in it.
/// </summary>
public interface IFlowClipImportService
{
    Task<FlowClipImportResult> ImportAsync(Guid contentProjectId, Guid sceneId, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<FlowImportStatus> GetStatusAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles a scene's "already has a video / skip Veo" flag. Turning it OFF
    /// also drops any imported clip (supersedes the scene's Video/Image assets
    /// and returns the scene to Pending) so it is genuinely generatable again.
    /// </summary>
    Task<StoryboardResponse> SetSkipGenerationAsync(Guid contentProjectId, Guid sceneId, bool skip, CancellationToken cancellationToken = default);
}

public class FlowClipImportService : IFlowClipImportService
{
    private const string FlowProvider = "google-flow";
    private const double NineBySixteen = 9.0 / 16.0;
    private const double AspectTolerance = 0.03;
    private const int TargetShortSidePx = 1080;

    private static readonly string[] AllowedExtensions = { ".mp4", ".mov", ".webm", ".m4v" };

    private readonly IStoryboardService _storyboardService;
    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IAssetService _assetService;
    private readonly IFileStorage _fileStorage;
    private readonly IMediaProbe _mediaProbe;
    private readonly ICreditLedger _creditLedger;
    private readonly CreditCostOptions _costs;
    private readonly ILogger<FlowClipImportService> _logger;

    public FlowClipImportService(
        IStoryboardService storyboardService,
        IStoryboardRepository storyboardRepository,
        IAssetService assetService,
        IFileStorage fileStorage,
        IMediaProbe mediaProbe,
        ICreditLedger creditLedger,
        IOptions<CreditCostOptions> costs,
        ILogger<FlowClipImportService> logger)
    {
        _storyboardService = storyboardService;
        _storyboardRepository = storyboardRepository;
        _assetService = assetService;
        _fileStorage = fileStorage;
        _mediaProbe = mediaProbe;
        _creditLedger = creditLedger;
        _costs = costs.Value;
        _logger = logger;
    }

    public async Task<FlowClipImportResult> ImportAsync(Guid contentProjectId, Guid sceneId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new DomainException($"Định dạng video không hỗ trợ '{extension}'. Cho phép: {string.Join(", ", AllowedExtensions)}.");
        }

        var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{contentProjectId}/scenes/{sceneId}/flow-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}",
            content,
            cancellationToken);

        var media = await _mediaProbe.ProbeAsync(_fileStorage.GetAbsolutePath(storedPath), cancellationToken);

        var issues = new List<string>();
        var warnings = new List<string>();

        if (!media.Ok || !media.HasVideo)
        {
            issues.Add($"file không phải video hợp lệ ({media.Error ?? "no video stream"})");
        }

        if (media.DurationSeconds <= 0)
        {
            issues.Add("clip có thời lượng 0 giây");
        }

        var (aspectLabel, ratio) = DescribeAspect(media.Width, media.Height);
        if (media.Width > 0 && media.Height > 0)
        {
            if (Math.Abs(ratio - NineBySixteen) > AspectTolerance)
            {
                warnings.Add($"tỷ lệ {aspectLabel} không phải 9:16 - sẽ bị thêm viền khi ghép");
            }

            if (Math.Min(media.Width, media.Height) < TargetShortSidePx)
            {
                warnings.Add($"độ phân giải {media.Width}x{media.Height} thấp hơn mục tiêu 1080p");
            }
        }
        else
        {
            warnings.Add("không đọc được độ phân giải");
        }

        if (issues.Count > 0)
        {
            // Nothing usable - don't keep the file or touch the scene.
            await _fileStorage.DeleteAsync(storedPath, cancellationToken);
            _logger.LogWarning("Rejected Flow clip for scene {SceneId}: {Issues}", sceneId, string.Join("; ", issues));
            return new FlowClipImportResult(sceneId, scene.SceneNumber, Accepted: false,
                media.DurationSeconds, media.Width, media.Height, aspectLabel, issues, warnings);
        }

        // Replace whatever this scene currently uses - AI clip or still.
        await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Video, cancellationToken);
        await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Image, cancellationToken);

        var asset = await _assetService.CreateAsync(
            contentProjectId,
            new CreateAssetRequest(sceneId, AssetType.Video, FlowProvider, Path.GetFileName(fileName), storedPath, media.DurationSeconds, media.Width, media.Height),
            cancellationToken);

        scene.MarkGenerated();
        // The user now has this clip - lock it out of every generation path
        // (full run, single-clip rerun, cost estimate) until they remove it.
        scene.SetSkipGeneration(true);
        await _storyboardRepository.SaveChangesAsync(cancellationToken);

        // Book the Flow credits this clip cost, so used/remaining reflect reality.
        var tier = Enum.TryParse<VideoModelTier>(scene.ModelTier, ignoreCase: true, out var t) ? t : VideoModelTier.Lite;
        await _creditLedger.RecordExternalCompletionAsync(
            new CreditReservationRequest(contentProjectId, sceneId, GenerationKind.Video, FlowProvider, "google-flow", tier),
            _costs.VideoCreditsFor(tier),
            asset.Id,
            cancellationToken);

        _logger.LogInformation(
            "Imported Flow clip for scene {SceneNumber} ({SceneId}): {Duration:0.0}s {W}x{H}",
            scene.SceneNumber, sceneId, media.DurationSeconds, media.Width, media.Height);

        return new FlowClipImportResult(sceneId, scene.SceneNumber, Accepted: true,
            media.DurationSeconds, media.Width, media.Height, aspectLabel, issues, warnings);
    }

    public async Task<FlowImportStatus> GetStatusAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
        var statuses = new List<FlowImportSceneStatus>(scenes.Count);
        var readyForRender = scenes.Count > 0;

        foreach (var scene in scenes)
        {
            var video = Current(assets, scene.Id, AssetType.Video);
            var still = Current(assets, scene.Id, AssetType.Image);

            var needsFlow = scene.GenerationType == "AI_VIDEO";
            var hasClip = video is not null;
            var clipValid = hasClip && (video!.DurationSeconds ?? 0) > 0;
            var hasAnyVisual = video is not null || still is not null;

            if (!hasAnyVisual)
            {
                readyForRender = false;
            }

            var issues = new List<string>();
            if (needsFlow && !hasClip)
            {
                issues.Add("cần clip từ Google Flow");
            }
            else if (hasClip && !clipValid)
            {
                issues.Add("clip không đọc được thời lượng - thử import lại");
            }
            else if (!needsFlow && !hasAnyVisual)
            {
                issues.Add("chưa có ảnh/clip cho cảnh này");
            }

            statuses.Add(new FlowImportSceneStatus(
                scene.Id, scene.SceneNumber, scene.GenerationType,
                needsFlow, hasClip, clipValid,
                video?.DurationSeconds, video?.Width, video?.Height,
                issues));
        }

        var scenesNeedingFlow = statuses.Count(s => s.NeedsFlowClip);
        var importedValid = statuses.Count(s => s.NeedsFlowClip && s.HasClip && s.ClipValid);

        return new FlowImportStatus(
            TotalScenes: scenes.Count,
            ScenesNeedingFlow: scenesNeedingFlow,
            ImportedValid: importedValid,
            MissingOrInvalid: scenesNeedingFlow - importedValid,
            ReadyForRender: readyForRender,
            Scenes: statuses);
    }

    public async Task<StoryboardResponse> SetSkipGenerationAsync(Guid contentProjectId, Guid sceneId, bool skip, CancellationToken cancellationToken = default)
    {
        var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        if (skip)
        {
            scene.SetSkipGeneration(true);
        }
        else
        {
            // Re-enable generation: retire whatever visual the scene currently
            // holds (an imported clip or a placeholder still) so nothing counts
            // it as done, and put it back to Pending.
            await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Video, cancellationToken);
            await _assetService.SupersedeSceneAssetsAsync(contentProjectId, sceneId, AssetType.Image, cancellationToken);
            scene.SetSkipGeneration(false);
            scene.MarkPending();
        }

        await _storyboardRepository.SaveChangesAsync(cancellationToken);

        var fresh = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard was not found after updating the scene.");
        return StoryboardResponse.FromDomain(fresh);
    }

    private static AssetResponse? Current(IReadOnlyList<AssetResponse> assets, Guid sceneId, AssetType type) =>
        assets
            .Where(a => a.SceneId == sceneId && a.Type == type.ToString() && a.Status == nameof(AssetStatus.Ready))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

    private static (string Label, double Ratio) DescribeAspect(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return ("unknown", 0);
        }

        var ratio = (double)width / height;
        var label = ratio switch
        {
            _ when Math.Abs(ratio - 9.0 / 16.0) <= AspectTolerance => "9:16",
            _ when Math.Abs(ratio - 16.0 / 9.0) <= AspectTolerance => "16:9",
            _ when Math.Abs(ratio - 1.0) <= AspectTolerance => "1:1",
            _ when Math.Abs(ratio - 4.0 / 5.0) <= AspectTolerance => "4:5",
            _ => $"{ratio:0.00}:1"
        };
        return (label, ratio);
    }
}
