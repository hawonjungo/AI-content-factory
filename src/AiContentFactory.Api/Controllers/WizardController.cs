using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Wizard;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// Endpoints the wizard uses. The pipeline-shaped endpoints on
/// ContentProjectsController stay exactly as they are for the Advanced page -
/// this is an additional, user-facing surface, not a replacement.
/// </summary>
[ApiController]
[Route("content-projects/{contentProjectId:guid}")]
public class WizardController : ControllerBase
{
    private readonly IProjectOverviewService _overviewService;
    private readonly IPresetService _presetService;
    private readonly IGenerationEstimator _estimator;
    private readonly IRenderService _renderService;
    private readonly IFlowGenerationPlanService _flowPlanService;
    private readonly IFlowClipImportService _flowClipImportService;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<WizardController> _logger;

    public WizardController(
        IProjectOverviewService overviewService,
        IPresetService presetService,
        IGenerationEstimator estimator,
        IRenderService renderService,
        IFlowGenerationPlanService flowPlanService,
        IFlowClipImportService flowClipImportService,
        IBackgroundJobClient backgroundJobClient,
        ILogger<WizardController> logger)
    {
        _overviewService = overviewService;
        _presetService = presetService;
        _estimator = estimator;
        _renderService = renderService;
        _flowPlanService = flowPlanService;
        _flowClipImportService = flowClipImportService;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    /// <summary>Everything the wizard renders, in one call.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<ProjectOverviewResponse>> GetOverview(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var overview = await _overviewService.GetAsync(contentProjectId, cancellationToken);
        return overview is null ? NotFound() : Ok(overview);
    }

    /// <summary>
    /// Step 5: the Google Flow Generation Plan - per-scene prompts, reference
    /// assets, recommended Fast/Lite model, and the credit budget, so the user
    /// can run the video-generation step in Flow deliberately.
    /// </summary>
    [HttpGet("flow-plan")]
    public async Task<ActionResult<FlowGenerationPlan>> GetFlowPlan(Guid contentProjectId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _flowPlanService.BuildAsync(contentProjectId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    /// <summary>Step 6: which scenes have a valid imported Flow clip, which are still missing/invalid.</summary>
    [HttpGet("flow-import-status")]
    public async Task<ActionResult<FlowImportStatus>> GetFlowImportStatus(Guid contentProjectId, CancellationToken cancellationToken) =>
        Ok(await _flowClipImportService.GetStatusAsync(contentProjectId, cancellationToken));

    [HttpPost("presets")]
    public async Task<ActionResult<ContentProjectResponse>> ApplyPresets(
        Guid contentProjectId,
        [FromBody] ApplyPresetsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _presetService.ApplyAsync(contentProjectId, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpGet("captions")]
    public async Task<ActionResult<CaptionSettingsDto>> GetCaptions(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var settings = await _presetService.GetCaptionSettingsAsync(contentProjectId, cancellationToken);
        return settings is null ? NotFound() : Ok(settings);
    }

    [HttpPut("captions")]
    public async Task<ActionResult<CaptionSettingsDto>> UpdateCaptions(
        Guid contentProjectId,
        [FromBody] CaptionSettingsDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _presetService.UpdateCaptionSettingsAsync(contentProjectId, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// Synchronous on purpose - this is one ffmpeg frame with no AI call, and
    /// the caption editor is unusable if the preview arrives via a job queue.
    /// </summary>
    [HttpPost("captions/preview")]
    public async Task<IActionResult> PreviewCaptions(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var asset = await _renderService.RenderCaptionPreviewAsync(contentProjectId, cancellationToken);

        return asset is null
            ? Problem("Chưa có clip hoặc ảnh mẫu để xem trước phụ đề.", statusCode: StatusCodes.Status409Conflict)
            : Ok(new { previewUrl = $"/content-projects/{contentProjectId}/assets/{asset.Id}/file" });
    }

    /// <param name="mode">"standard" (default) or "googleflow".</param>
    [HttpGet("estimate")]
    public async Task<ActionResult<GenerationEstimate>> GetEstimate(Guid contentProjectId, [FromQuery] string? mode, CancellationToken cancellationToken)
    {
        var estimate = await _estimator.EstimateProjectAsync(contentProjectId, GenerationModeParser.Parse(mode), cancellationToken);
        return Ok(estimate);
    }

    [HttpGet("clips/{sceneId:guid}/estimate")]
    public async Task<ActionResult<GenerationEstimate>> GetClipEstimate(
        Guid contentProjectId,
        Guid sceneId,
        [FromQuery] bool includeVoice,
        CancellationToken cancellationToken)
    {
        try
        {
            var estimate = await _estimator.EstimateClipAsync(contentProjectId, sceneId, includeVoice, cancellationToken);
            return Ok(estimate);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    public record RegenerateClipBody(string? Narration, bool RegenerateVoice = false);

    [HttpPost("clips/{sceneId:guid}/regenerate")]
    public IActionResult RegenerateClip(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] RegenerateClipBody? body)
    {
        // Hangfire builds an expression tree from this lambda, which can't
        // contain null-propagation - so both values are resolved first.
        var narration = body?.Narration;
        var regenerateVoice = body?.RegenerateVoice ?? false;

        var jobId = _backgroundJobClient.Enqueue<ClipRegenerationJob>(job =>
            job.RunAsync(contentProjectId, sceneId, narration, regenerateVoice));

        _logger.LogInformation(
            "Enqueued clip regeneration job {JobId} for scene {SceneId} on ContentProject {ContentProjectId}",
            jobId, sceneId, contentProjectId);

        // The job id is deliberately not returned - the wizard tracks progress
        // through /overview, and a Hangfire id is exactly the kind of internal
        // detail this surface is meant to hide.
        return Accepted();
    }
}
