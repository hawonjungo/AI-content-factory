using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Qa;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Generation;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Wizard;

public interface IProjectOverviewService
{
    Task<ProjectOverviewResponse?> GetAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One endpoint behind the whole wizard. Assembles project, script, storyboard,
/// assets, QA, references, the credit summary, the generation-attempt log, and
/// the composition/validation status - and keeps the privacy rule enforceable
/// in one place: no provider names except where deliberately surfaced (the
/// attempt log, which the user needs to diagnose a failure), no storage paths,
/// no job ids.
/// </summary>
public class ProjectOverviewService : IProjectOverviewService
{
    private readonly IContentProjectRepository _projectRepository;
    private readonly IScriptService _scriptService;
    private readonly IStoryboardService _storyboardService;
    private readonly IAssetService _assetService;
    private readonly IQaService _qaService;
    private readonly IGenerationEstimator _estimator;
    private readonly IAssetReferenceService _assetReferenceService;
    private readonly ICreditLedger _creditLedger;
    private readonly IGenerationAttemptRepository _attemptRepository;
    private readonly CreditCostOptions _creditCosts;
    private readonly QaOptions _qaOptions;
    private readonly VideoGenerationOptions _videoOptions;

    public ProjectOverviewService(
        IContentProjectRepository projectRepository,
        IScriptService scriptService,
        IStoryboardService storyboardService,
        IAssetService assetService,
        IQaService qaService,
        IGenerationEstimator estimator,
        IAssetReferenceService assetReferenceService,
        ICreditLedger creditLedger,
        IGenerationAttemptRepository attemptRepository,
        IOptions<CreditCostOptions> creditCosts,
        IOptions<QaOptions> qaOptions,
        IOptions<VideoGenerationOptions> videoOptions)
    {
        _projectRepository = projectRepository;
        _scriptService = scriptService;
        _storyboardService = storyboardService;
        _assetService = assetService;
        _qaService = qaService;
        _estimator = estimator;
        _assetReferenceService = assetReferenceService;
        _creditLedger = creditLedger;
        _attemptRepository = attemptRepository;
        _creditCosts = creditCosts.Value;
        _qaOptions = qaOptions.Value;
        _videoOptions = videoOptions.Value;
    }

    public async Task<ProjectOverviewResponse?> GetAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var script = await _scriptService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        var qaScores = await _qaService.GetHistoryAsync(contentProjectId, cancellationToken);
        var referenceSlots = await _assetReferenceService.GetSlotsAsync(contentProjectId, cancellationToken);
        var referencesResolved = await _assetReferenceService.AreBothResolvedAsync(contentProjectId, cancellationToken);
        var estimate = await _estimator.EstimateProjectAsync(contentProjectId, GenerationMode.Standard, cancellationToken);
        var creditUsage = await _creditLedger.GetDailyUsageAsync(cancellationToken);
        var attempts = await _attemptRepository.GetByProjectAsync(contentProjectId, cancellationToken);

        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
        var busy = !project.Progress.IsIdle;

        var clips = scenes
            .Select(scene => BuildClip(contentProjectId, scene, assets, busy, project.Progress.Stage))
            .ToList();

        var finalVideo = assets
            .Where(a => a.SceneId is null && a.Type == nameof(AssetType.Video) && a.Status == nameof(AssetStatus.Ready))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

        var captionPreview = assets
            .Where(a => a.SceneId is null && a.Type == nameof(AssetType.Image) && a.Provider == AssetProviders.CaptionPreview && a.Status == nameof(AssetStatus.Ready))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

        var facts = new WizardFacts(
            HasTemplate: !string.IsNullOrWhiteSpace(project.TemplateId),
            HasScript: script is not null,
            ReferencesResolved: referencesResolved,
            HasClipPlan: scenes.Count > 0,
            TotalClips: scenes.Count,
            ReadyClips: clips.Count(c => c.State == nameof(ClipState.Ready)),
            HasFinalVideo: finalVideo is not null);

        var state = WizardStepResolver.Resolve(project.Status, facts);
        var latestQa = qaScores.FirstOrDefault();

        var estimatedCredits = EstimateProjectCredits(scenes);
        var credits = new WizardCreditSummaryDto(
            creditUsage.DailyBudget,
            creditUsage.Reserved,
            creditUsage.Used,
            creditUsage.Remaining,
            creditUsage.FailedToday,
            estimatedCredits,
            WithinBudget: estimatedCredits == 0 || estimatedCredits <= creditUsage.Remaining);

        var sceneNumberById = scenes.ToDictionary(s => s.Id, s => s.SceneNumber);
        var attemptsDto = attempts
            .Select(a => new WizardAttemptDto(
                a.Id,
                a.Kind.ToString(),
                a.SceneId is { } sid && sceneNumberById.TryGetValue(sid, out var n) ? n : null,
                a.Provider,
                a.Model,
                a.ModelTier,
                a.Status.ToString(),
                a.EstimatedCredits,
                a.ActualCredits,
                a.AttemptNumber,
                a.FailureReason,
                a.AudioDurationSeconds,
                a.CreatedAt))
            .ToList();

        var composition = BuildCompositionStatus(project, scenes, assets, finalVideo is not null, busy);
        var lastValidation = BuildValidation(project.LastRenderValidation);

        return new ProjectOverviewResponse(
            project.Id,
            project.Title,
            project.Topic,
            project.Niche,
            project.TargetDurationSeconds,
            project.AspectRatio,
            project.Language,
            BuildPresetSelection(project),
            CaptionSettingsDto.FromDomain(project.Captions),
            ContentIdeaConfigDto.FromDomain(project.IdeaConfig),
            state.Step.ToString(),
            state.ReachableSteps.Select(s => s.ToString()).ToList(),
            WizardStepResolver.Describe(project.Progress),
            script,
            clips,
            referenceSlots,
            FileUrl(contentProjectId, finalVideo?.Id),
            FileUrl(contentProjectId, captionPreview?.Id),
            assets.Any(a => a.SceneId is null && a.Type == nameof(AssetType.Music) && a.Status == nameof(AssetStatus.Ready)),
            _videoOptions.GoogleFlowEnabled,
            estimate,
            credits,
            attemptsDto,
            composition,
            lastValidation,
            latestQa is null
                ? null
                : new WizardQaDto(
                    latestQa.Overall,
                    WizardStepResolver.DescribeQaScore(latestQa.Overall, _qaOptions.MinimumOverallScoreToProceed),
                    latestQa.Notes),
            state.Blockers,
            project.Status == ContentProjectStatus.Failed);
    }

    private int EstimateProjectCredits(IReadOnlyList<SceneResponse> scenes)
    {
        var total = 0;
        foreach (var scene in scenes)
        {
            // A clip the user already has a video for draws no credits.
            if (scene.SkipGeneration)
            {
                continue;
            }

            if (string.Equals(scene.VisualType, nameof(Domain.Storyboards.SceneVisualType.AiVideo), StringComparison.OrdinalIgnoreCase))
            {
                var tier = Enum.TryParse<VideoModelTier>(scene.ModelTier, ignoreCase: true, out var t) ? t : VideoModelTier.Lite;
                total += _creditCosts.VideoCreditsFor(tier);
            }
            else
            {
                total += _creditCosts.ImageCredits;
            }
        }

        return total;
    }

    private static WizardCompositionStatusDto BuildCompositionStatus(
        ContentProject project,
        IReadOnlyList<SceneResponse> scenes,
        IReadOnlyList<AssetResponse> assets,
        bool hasFinalVideo,
        bool busy)
    {
        var expecting = scenes.Count(s => !string.IsNullOrWhiteSpace(s.Narration));
        var sceneIdsWithVoice = assets
            .Where(a => a.SceneId is not null && a.Type == nameof(AssetType.Voice) && a.Status == nameof(AssetStatus.Ready))
            .Select(a => a.SceneId!.Value)
            .ToHashSet();
        var withNarration = scenes.Count(s => sceneIdsWithVoice.Contains(s.Id));
        var narrationSeconds = assets
            .Where(a => a.SceneId is not null && a.Type == nameof(AssetType.Voice) && a.Status == nameof(AssetStatus.Ready))
            .Sum(a => a.DurationSeconds ?? 0);

        var narrationStatus = expecting == 0
            ? "none"
            : withNarration == 0 ? "missing"
            : withNarration < expecting ? "partial"
            : "ready";

        var validation = project.LastRenderValidation;
        var captionStatus = !project.Captions.Enabled
            ? "disabled"
            : hasFinalVideo ? "burned" : "pending";

        var renderStatus = busy && project.Progress.Stage == RenderService.RenderStage
            ? "running"
            : validation.HasRun && validation.Ok ? "completed"
            : validation.HasRun ? "failed"
            : "pending";

        var validationStatus = validation.HasRun ? (validation.Ok ? "passed" : "failed") : "pending";
        var compositionStatus = renderStatus == "running" ? "running" : renderStatus;

        return new WizardCompositionStatusDto(
            narrationStatus,
            Math.Round(narrationSeconds, 2),
            withNarration,
            expecting,
            captionStatus,
            compositionStatus,
            renderStatus,
            validationStatus,
            validation.Errors.ToList(),
            validation.Warnings.ToList());
    }

    private static WizardValidationDto BuildValidation(RenderValidationSummary summary) => new(
        summary.HasRun,
        summary.Ok,
        summary.Summary,
        summary.DurationSeconds,
        summary.Errors.ToList(),
        summary.Warnings.ToList(),
        summary.CheckedAt);

    private static WizardPresetSelectionDto BuildPresetSelection(ContentProject project) => new(
        project.TemplateId,
        PresetCatalog.FindTemplate(project.TemplateId)?.Name,
        project.StylePresetId,
        PresetCatalog.FindStyle(project.StylePresetId)?.Name,
        project.VoicePresetId,
        PresetCatalog.FindVoice(project.VoicePresetId)?.Name,
        project.CaptionPresetId,
        PresetCatalog.FindCaption(project.CaptionPresetId)?.Name);

    private static WizardClipDto BuildClip(
        Guid contentProjectId,
        SceneResponse scene,
        IReadOnlyList<AssetResponse> assets,
        bool busy,
        string stage)
    {
        var video = Current(assets, scene.Id, AssetType.Video);
        var still = Current(assets, scene.Id, AssetType.Image);
        var visual = video ?? still;
        var voice = Current(assets, scene.Id, AssetType.Voice);

        var state = ResolveClipState(scene, visual, busy, stage);

        return new WizardClipDto(
            scene.Id,
            scene.SceneNumber,
            scene.Narration,
            scene.DurationSeconds,
            state.ToString(),
            scene.VisualType,
            scene.GenerationPrompt,
            state == ClipState.Ready ? FileUrl(contentProjectId, visual?.Id) : null,
            voice is not null,
            scene.AiVideoPriority,
            scene.ModelTier,
            scene.CameraMovement,
            scene.AllocationRationale,
            scene.SkipGeneration,
            HasExistingVideo: video is not null);
    }

    private static ClipState ResolveClipState(SceneResponse scene, AssetResponse? visual, bool busy, string stage)
    {
        if (visual is not null)
        {
            return ClipState.Ready;
        }

        if (scene.Status == nameof(Domain.Storyboards.SceneStatus.Failed))
        {
            return ClipState.Failed;
        }

        var clipStageRunning = busy &&
            (stage == Generation.AssetGenerationService.ClipsStage || stage == Generation.ClipRegenerationService.ClipStage);

        return clipStageRunning ? ClipState.Working : ClipState.NotStarted;
    }

    private static AssetResponse? Current(IReadOnlyList<AssetResponse> assets, Guid sceneId, AssetType type) =>
        assets
            .Where(a => a.SceneId == sceneId && a.Type == type.ToString() && a.Status == nameof(AssetStatus.Ready))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault();

    private static string? FileUrl(Guid contentProjectId, Guid? assetId) =>
        assetId is null ? null : $"/content-projects/{contentProjectId}/assets/{assetId}/file";
}
