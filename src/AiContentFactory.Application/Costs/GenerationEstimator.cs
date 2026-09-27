using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Costs;

/// <param name="EstimatedSeconds">Wall-clock wait for this step, NOT playback length.</param>
/// <param name="EstimatedCredits">Draw on the daily free-credit pool.</param>
public record EstimateLineItem(string Label, int Quantity, decimal EstimatedCostUsd, int EstimatedSeconds, int EstimatedCredits);

/// <param name="TotalSeconds">
/// Estimated wall-clock WAIT to build the video (clips are generated
/// sequentially). NOT how long the finished video is - that is
/// <see cref="OutputVideoSeconds"/>.
/// </param>
/// <param name="OutputVideoSeconds">Playback length of the finished video.</param>
/// <param name="TotalCredits">Reference-only draw on Google's Flow-tier daily credit figure - no longer enforced.</param>
/// <param name="DailyCredits">Size of the daily pool (reference only).</param>
/// <param name="DailyCreditsRemaining">What's left in the pool today (account-wide, reference only).</param>
/// <param name="WithinCredits">Whether this run fits in the credits left today (reference only - never blocks).</param>
/// <param name="FreeWithinQuota">True when <see cref="TotalCostUsd"/> is $0 (e.g. nothing pending, or images only).</param>
/// <param name="TotalCostUsd">
/// Real estimated USD cost of this run - Veo bills per second generated, so
/// this is not a "free until you exceed a credit pool" figure.
/// </param>
/// <param name="Mode">"Standard" or "GoogleFlow".</param>
/// <param name="ClipPlanIgnored">True when Google Flow writes its own fixed script (no clip plan yet).</param>
/// <param name="WithinBudget">False when <see cref="TotalCostUsd"/> would cross the monthly USD ceiling.</param>
public record GenerationEstimate(
    IReadOnlyList<EstimateLineItem> LineItems,
    decimal TotalCostUsd,
    int TotalSeconds,
    int OutputVideoSeconds,
    int TotalCredits,
    int DailyCredits,
    int DailyCreditsRemaining,
    bool WithinCredits,
    bool FreeWithinQuota,
    int ClipCount,
    int VideoSceneCount,
    int ImageSceneCount,
    int AlreadyGeneratedClips,
    string Mode,
    bool ClipPlanIgnored,
    decimal? MonthlyBudgetRemainingUsd,
    int? DailyQuotaRemainingVideos,
    bool WithinBudget);

public interface IGenerationEstimator
{
    /// <summary>Cost, wall-clock and credit estimate for generating everything the project still needs, in the given mode.</summary>
    Task<GenerationEstimate> EstimateProjectAsync(Guid contentProjectId, GenerationMode mode = GenerationMode.Standard, CancellationToken cancellationToken = default);

    /// <summary>Estimate for rerunning exactly one clip (respects the scene's video/image type).</summary>
    Task<GenerationEstimate> EstimateClipAsync(Guid contentProjectId, Guid sceneId, bool includeVoice, CancellationToken cancellationToken = default);
}

/// <summary>
/// Answers "what will this cost me, how long will I wait, and how much of
/// today's free credits does it use" BEFORE anything is spent.
///
/// Everything here is an estimate from configured assumptions
/// (<see cref="PricingOptions"/> / <see cref="GoogleFlowOptions"/>), not a
/// quote - actual provider billing wins.
/// </summary>
public class GenerationEstimator : IGenerationEstimator
{
    private readonly IStoryboardService _storyboardService;
    private readonly IAssetService _assetService;
    private readonly IAiUsageTracker _usageTracker;
    private readonly IGoogleFlowQuotaManager _quotaManager;
    private readonly PricingOptions _pricing;
    private readonly BudgetOptions _budget;
    private readonly GoogleFlowOptions _flow;
    private readonly ILogger<GenerationEstimator> _logger;
    private readonly ContentProjects.IContentProjectRepository? _projectRepository;

    public GenerationEstimator(
        IStoryboardService storyboardService,
        IAssetService assetService,
        IAiUsageTracker usageTracker,
        IGoogleFlowQuotaManager quotaManager,
        IOptions<PricingOptions> pricing,
        IOptions<BudgetOptions> budget,
        IOptions<GoogleFlowOptions> flow,
        ILogger<GenerationEstimator> logger,
        ContentProjects.IContentProjectRepository? projectRepository = null)
    {
        _projectRepository = projectRepository;
        _storyboardService = storyboardService;
        _assetService = assetService;
        _usageTracker = usageTracker;
        _quotaManager = quotaManager;
        _pricing = pricing.Value;
        _budget = budget.Value;
        _flow = flow.Value;
        _logger = logger;
    }

    /// <summary>True when the project's resolved voice is a free self-hosted one (same resolution as the real run).</summary>
    private async Task<bool> UsesFreeVoiceAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        if (_projectRepository is null)
        {
            return false;
        }

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        return project is not null && Presets.PresetCatalog.ResolveVoice(project.VoicePresetId, project.IdeaConfig.VoiceGender).IsFree;
    }

    // Google Flow always writes this shape - see HookScriptAgent.
    private const int GoogleFlowClipCount = 3;
    private static readonly int[] GoogleFlowClipDurations = { 5, 10, 5 };
    private const int GoogleFlowNarrationCharsPerClip = 120;

    public Task<GenerationEstimate> EstimateProjectAsync(Guid contentProjectId, GenerationMode mode = GenerationMode.Standard, CancellationToken cancellationToken = default) =>
        mode == GenerationMode.GoogleFlow
            ? EstimateGoogleFlowAsync(contentProjectId, cancellationToken)
            : EstimateStandardAsync(contentProjectId, cancellationToken);

    private async Task<GenerationEstimate> EstimateStandardAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
        var lineItems = new List<EstimateLineItem>();

        // A scene still needs work if it has no ready visual of EITHER kind -
        // the user may have flipped its type since it was last generated. A
        // scene the user already has a clip for (SkipGeneration) is never
        // generated, so it never contributes a generation cost.
        var pending = scenes.Where(s => !HasReadyVisual(assets, s.Id) && !s.SkipGeneration).ToList();
        var pendingVideo = pending.Where(s => VisualTypeOf(s) == SceneVisualType.AiVideo).ToList();
        var pendingImage = pending.Where(s => VisualTypeOf(s) == SceneVisualType.AiImage).ToList();

        if (pendingVideo.Count > 0)
        {
            // Cost is the sum of EACH clip priced at its own model tier - Fast
            // and Lite are ~5x apart, so a single blended rate is wrong.
            var videoCostUsd = pendingVideo.Sum(s => _pricing.VideoUsdPerSecondFor(TierOf(s)) * s.DurationSeconds);
            lineItems.Add(new EstimateLineItem(
                "Dựng clip video (Veo)",
                pendingVideo.Count,
                videoCostUsd, // Veo bills real USD per second, not credits
                _pricing.SecondsPerClipGeneration * pendingVideo.Count,
                _flow.CreditsPerVideoClip * pendingVideo.Count));
        }

        if (pendingImage.Count > 0)
        {
            lineItems.Add(new EstimateLineItem(
                "Tạo ảnh tĩnh (rẻ hơn nhiều)",
                pendingImage.Count,
                _pricing.ImageUsd * pendingImage.Count,
                _pricing.SecondsPerImage * pendingImage.Count,
                _flow.CreditsPerImage * pendingImage.Count));
        }

        var pendingVoice = scenes
            .Where(s => !string.IsNullOrWhiteSpace(s.Narration) && !HasReadyAsset(assets, s.Id, AssetType.Voice))
            .ToList();

        if (pendingVoice.Count > 0)
        {
            var characters = pendingVoice.Sum(s => s.Narration.Length);
            // Same rule the real run records (SceneAssetGenerator.GenerateVoiceAsync):
            // Gemini voices at the configured rate, a free Kokoro voice at $0.
            var freeVoice = await UsesFreeVoiceAsync(contentProjectId, cancellationToken);
            lineItems.Add(new EstimateLineItem(
                freeVoice ? "Lồng tiếng (giọng 🆓 Kokoro)" : "Lồng tiếng AI (Gemini)",
                pendingVoice.Count,
                freeVoice ? 0m : _pricing.TtsUsdPer1000Chars * characters / 1000m,
                _pricing.SecondsPerVoiceClip * pendingVoice.Count,
                0));
        }

        if (scenes.Count > 0)
        {
            lineItems.Add(new EstimateLineItem("Ghép và xuất video", 1, 0m, _pricing.RenderSecondsPerClip * scenes.Count, 0));
        }

        return await BuildAsync(
            lineItems,
            outputVideoSeconds: scenes.Sum(s => s.DurationSeconds),
            clipCount: scenes.Count,
            videoSceneCount: scenes.Count(s => VisualTypeOf(s) == SceneVisualType.AiVideo),
            imageSceneCount: scenes.Count(s => VisualTypeOf(s) == SceneVisualType.AiImage),
            alreadyGenerated: scenes.Count - pending.Count,
            mode: GenerationMode.Standard,
            cancellationToken);
    }

    private async Task<GenerationEstimate> EstimateGoogleFlowAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        // Google Flow now honours an existing clip plan (image -> video for
        // every scene). Only falls back to its own fixed 3 x ~20s hook script
        // when there is no plan yet.
        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
        var usingUserPlan = scenes.Count > 0;

        var clipCount = usingUserPlan ? scenes.Count : GoogleFlowClipCount;
        var outputSeconds = usingUserPlan ? scenes.Sum(s => s.DurationSeconds) : GoogleFlowClipDurations.Sum();

        var lineItems = new List<EstimateLineItem>
        {
            // The image here is an intermediate step turned straight into
            // video, so it does not draw credits on its own.
            new("Tạo ảnh nền (Nano Banana)", clipCount, 0m, _pricing.SecondsPerImage * clipCount, 0),
            new(
                "Chuyển ảnh thành video (Veo)",
                clipCount,
                _pricing.VideoUsdPerSecond * outputSeconds,
                _pricing.GoogleFlowSecondsPerClip * clipCount,
                _flow.CreditsPerVideoClip * clipCount),
            // This mode always voices with Gemini (GoogleFlowAssetGenerationService), billed per character.
            new("Lồng tiếng (Gemini TTS)", clipCount, _pricing.TtsUsdPer1000Chars * GoogleFlowNarrationCharsPerClip * clipCount / 1000m, _pricing.SecondsPerVoiceClip * clipCount, 0),
            new("Ghép và xuất video", 1, 0m, _pricing.RenderSecondsPerClip * clipCount, 0)
        };

        return await BuildAsync(
            lineItems,
            outputVideoSeconds: outputSeconds,
            clipCount: clipCount,
            videoSceneCount: clipCount,
            imageSceneCount: 0,
            alreadyGenerated: 0,
            mode: GenerationMode.GoogleFlow,
            cancellationToken,
            clipPlanIgnored: !usingUserPlan);
    }

    public async Task<GenerationEstimate> EstimateClipAsync(Guid contentProjectId, Guid sceneId, bool includeVoice, CancellationToken cancellationToken = default)
    {
        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new InvalidOperationException($"Clip '{sceneId}' was not found on this project.");

        var isVideo = VisualTypeOf(scene) == SceneVisualType.AiVideo;

        var lineItems = new List<EstimateLineItem>
        {
            isVideo
                ? new EstimateLineItem("Dựng lại clip video", 1, _pricing.VideoUsdPerSecondFor(TierOf(scene)) * scene.DurationSeconds, _pricing.SecondsPerClipGeneration, _flow.CreditsPerVideoClip)
                : new EstimateLineItem("Tạo lại ảnh tĩnh", 1, _pricing.ImageUsd, _pricing.SecondsPerImage, _flow.CreditsPerImage)
        };

        if (includeVoice && !string.IsNullOrWhiteSpace(scene.Narration))
        {
            var freeVoice = await UsesFreeVoiceAsync(contentProjectId, cancellationToken);
            lineItems.Add(new EstimateLineItem(
                freeVoice ? "Lồng tiếng lại (giọng 🆓 Kokoro)" : "Lồng tiếng lại (Gemini)",
                1,
                freeVoice ? 0m : _pricing.TtsUsdPer1000Chars * scene.Narration.Length / 1000m,
                _pricing.SecondsPerVoiceClip,
                0));
        }

        return await BuildAsync(
            lineItems,
            outputVideoSeconds: scene.DurationSeconds,
            clipCount: 1,
            videoSceneCount: isVideo ? 1 : 0,
            imageSceneCount: isVideo ? 0 : 1,
            alreadyGenerated: 0,
            mode: GenerationMode.Standard,
            cancellationToken);
    }

    private async Task<GenerationEstimate> BuildAsync(
        IReadOnlyList<EstimateLineItem> lineItems,
        int outputVideoSeconds,
        int clipCount,
        int videoSceneCount,
        int imageSceneCount,
        int alreadyGenerated,
        GenerationMode mode,
        CancellationToken cancellationToken,
        bool clipPlanIgnored = false)
    {
        var totalSeconds = lineItems.Sum(i => i.EstimatedSeconds);
        var totalCredits = lineItems.Sum(i => i.EstimatedCredits);
        var totalCostUsd = decimal.Round(lineItems.Sum(i => i.EstimatedCostUsd), 2);

        var spent = await _usageTracker.GetCurrentMonthSpendAsync(cancellationToken);
        var budgetRemaining = _budget.MonthlyLimitUsd > 0 ? Math.Max(0m, _budget.MonthlyLimitUsd - spent) : (decimal?)null;

        // Credit lookup is reference info only now - it no longer gates
        // anything. Best-effort: a quota backend that's down must not stop the
        // user seeing an estimate.
        var creditsRemaining = _flow.DailyCredits;
        try
        {
            creditsRemaining = await _quotaManager.GetRemainingDailyCreditsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Daily credit pool unavailable while estimating");
        }

        var perVideo = Math.Max(1, _flow.CreditsPerVideoClip);

        return new GenerationEstimate(
            lineItems,
            totalCostUsd,
            totalSeconds,
            outputVideoSeconds,
            totalCredits,
            _flow.DailyCredits,
            creditsRemaining,
            totalCredits <= creditsRemaining,
            totalCostUsd == 0m,
            clipCount,
            videoSceneCount,
            imageSceneCount,
            alreadyGenerated,
            mode.ToString(),
            ClipPlanIgnored: clipPlanIgnored,
            budgetRemaining is null ? null : decimal.Round(budgetRemaining.Value, 2),
            Math.Max(0, creditsRemaining / perVideo),
            budgetRemaining is null || totalCostUsd <= budgetRemaining.Value);
    }

    private static SceneVisualType VisualTypeOf(SceneResponse scene) =>
        Enum.TryParse<SceneVisualType>(scene.VisualType, ignoreCase: true, out var parsed) ? parsed : SceneVisualType.AiVideo;

    private static Domain.Generation.VideoModelTier TierOf(SceneResponse scene) =>
        Enum.TryParse<Domain.Generation.VideoModelTier>(scene.ModelTier, ignoreCase: true, out var tier)
            ? tier
            : Domain.Generation.VideoModelTier.Lite;

    private static bool HasReadyAsset(IReadOnlyList<AssetResponse> assets, Guid sceneId, AssetType type) =>
        assets.Any(a => a.SceneId == sceneId && a.Type == type.ToString() && a.Status == nameof(AssetStatus.Ready));

    private static bool HasReadyVisual(IReadOnlyList<AssetResponse> assets, Guid sceneId) =>
        HasReadyAsset(assets, sceneId, AssetType.Video) || HasReadyAsset(assets, sceneId, AssetType.Image);
}
