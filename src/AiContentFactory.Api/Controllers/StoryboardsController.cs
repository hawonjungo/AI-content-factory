using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/storyboard")]
public class StoryboardsController : ControllerBase
{
    private readonly IStoryboardService _service;
    private readonly IClipPlanService _clipPlanService;
    private readonly IFlowClipImportService _flowClipImportService;
    private readonly ISceneKeyframeService _keyframeService;
    private readonly IProjectJobReservationService _jobReservation;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<StoryboardsController> _logger;

    public StoryboardsController(
        IStoryboardService service,
        IClipPlanService clipPlanService,
        IFlowClipImportService flowClipImportService,
        ISceneKeyframeService keyframeService,
        IProjectJobReservationService jobReservation,
        IBackgroundJobClient backgroundJobClient,
        ILogger<StoryboardsController> logger)
    {
        _service = service;
        _clipPlanService = clipPlanService;
        _flowClipImportService = flowClipImportService;
        _keyframeService = keyframeService;
        _jobReservation = jobReservation;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<StoryboardResponse>> Get(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var storyboard = await _service.GetOrCreateAsync(contentProjectId, cancellationToken);
        return Ok(storyboard);
    }

    [HttpPost("clip-plan")]
    public async Task<ActionResult<StoryboardResponse>> GenerateClipPlan(
        Guid contentProjectId,
        [FromBody] GenerateClipPlanRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _clipPlanService.GenerateAsync(contentProjectId, request, cancellationToken);

            // "Chia clip" replaces the storyboard's scenes every time it runs,
            // so any prompts the old scenes had are gone too - auto-chain the
            // same bulk prompt-suggestion job the manual "Tạo gợi ý cho tất cả
            // cảnh" button triggers, so the user doesn't have to click it
            // separately after every re-split.
            await EnqueueAutoPromptSuggestionsAsync(contentProjectId, cancellationToken);

            return Ok(result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    /// <summary>
    /// Fire-and-forget chain trigger for <see cref="SuggestAllScenePromptsJob"/>
    /// right after a successful clip-plan generation. Clip-plan generation
    /// itself is fast/deterministic/free and has no busy-gate of its own; this
    /// only reserves the project for the follow-up job. If the project is
    /// already busy with some other job (or was deleted concurrently), the
    /// reservation returns null and the auto-chain is skipped silently
    /// (logged) - the clip-plan response above must still succeed regardless,
    /// and the user can always trigger "Tạo gợi ý cho tất cả cảnh" manually
    /// later.
    /// </summary>
    private async Task EnqueueAutoPromptSuggestionsAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var project = await _jobReservation.ReserveAsync(
            contentProjectId,
            StoryboardService.PromptsStage,
            "Đang tự động tạo gợi ý prompt cho các cảnh",
            cancellationToken);

        if (project is null)
        {
            _logger.LogInformation(
                "Skipped auto-chaining bulk scene-prompt suggestion job for ContentProject {ContentProjectId} after clip-plan generation: project busy or not found.",
                contentProjectId);
            return;
        }

        var jobId = _backgroundJobClient.Enqueue<SuggestAllScenePromptsJob>(job => job.RunAsync(contentProjectId));
        _logger.LogInformation(
            "Auto-enqueued bulk scene-prompt suggestion job {JobId} for ContentProject {ContentProjectId} after clip-plan generation",
            jobId, contentProjectId);
    }

    [HttpPost("scenes")]
    public async Task<ActionResult<StoryboardResponse>> AddScene(
        Guid contentProjectId,
        [FromBody] CreateSceneRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.AddSceneAsync(contentProjectId, request, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    [HttpPut("scenes/{sceneId:guid}")]
    public async Task<ActionResult<StoryboardResponse>> UpdateScene(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] UpdateSceneRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.UpdateSceneAsync(contentProjectId, sceneId, request, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetVisualTypeBody(string VisualType);

    /// <summary>Switch one scene between AI video and AI still image (the cost lever).</summary>
    [HttpPut("scenes/{sceneId:guid}/visual-type")]
    public async Task<ActionResult<StoryboardResponse>> SetVisualType(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetVisualTypeBody body,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Domain.Storyboards.SceneVisualType>(body.VisualType, ignoreCase: true, out var visualType))
        {
            return Problem($"Unknown visual type '{body.VisualType}'.", statusCode: 400);
        }

        try
        {
            var result = await _service.SetSceneVisualTypeAsync(contentProjectId, sceneId, visualType, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetModelTierBody(string ModelTier);

    /// <summary>Pick the Veo model tier (Fast / Lite) for one video clip - this is the per-clip cost lever.</summary>
    [HttpPut("scenes/{sceneId:guid}/model")]
    public async Task<ActionResult<StoryboardResponse>> SetModelTier(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetModelTierBody body,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Domain.Generation.VideoModelTier>(body.ModelTier, ignoreCase: true, out var tier))
        {
            return Problem($"Unknown model tier '{body.ModelTier}'.", statusCode: 400);
        }

        try
        {
            var result = await _service.SetSceneModelTierAsync(contentProjectId, sceneId, tier.ToString(), cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetCameraMovementBody(string CameraMovement);

    /// <summary>Pick the deterministic camera movement for one scene - drives the single "Camera:" line of the video prompt.</summary>
    [HttpPut("scenes/{sceneId:guid}/camera")]
    public async Task<ActionResult<StoryboardResponse>> SetCameraMovement(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetCameraMovementBody body,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Domain.Storyboards.CameraMovement>(body.CameraMovement, ignoreCase: true, out var camera))
        {
            return Problem($"Unknown camera movement '{body.CameraMovement}'.", statusCode: 400);
        }

        try
        {
            var result = await _service.SetSceneCameraMovementAsync(contentProjectId, sceneId, camera, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    /// <summary>Request cap for a first-frame upload: the 10 MB image cap plus multipart overhead.</summary>
    private const int MaxFirstFrameRequestBytes = AiContentFactory.Application.Storage.UploadedImage.MaxBytes + 1024 * 1024;

    /// <summary>
    /// Free (no AI). Stores an image the user made in Google Flow as this
    /// scene's first frame (approved Keyframe), for Flow's "Frames to Video".
    /// </summary>
    [HttpPost("scenes/{sceneId:guid}/first-frame")]
    [RequestSizeLimit(MaxFirstFrameRequestBytes)]
    public async Task<ActionResult<SceneResponse>> UploadFirstFrame(
        Guid contentProjectId,
        Guid sceneId,
        IFormFile file,
        [FromServices] AiContentFactory.Application.Generation.IFlowKeyframeService flowKeyframes,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem("Chưa chọn file ảnh.", statusCode: 400);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await flowKeyframes.UploadFirstFrameAsync(contentProjectId, sceneId, file.FileName, stream, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    /// <summary>
    /// Free (local FFmpeg, no AI). Uses the last frame of the previous scene's
    /// clip as this scene's first frame, so consecutive clips join smoothly.
    /// </summary>
    [HttpPost("scenes/{sceneId:guid}/first-frame/from-previous-clip")]
    public async Task<ActionResult<SceneResponse>> UsePreviousClipLastFrame(
        Guid contentProjectId,
        Guid sceneId,
        [FromServices] AiContentFactory.Application.Generation.IFlowKeyframeService flowKeyframes,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await flowKeyframes.UsePreviousClipLastFrameAsync(contentProjectId, sceneId, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    /// <summary>
    /// Billable (one vision call, see Pricing:ClipCheckUsd): checks the scene's
    /// current clip against its references and intended shot. A stored result
    /// for the same clip is returned for free unless force=true.
    /// </summary>
    [HttpPost("scenes/{sceneId:guid}/clip-check")]
    public async Task<ActionResult<AiContentFactory.Application.Generation.ClipCheckResult>> CheckClip(
        Guid contentProjectId,
        Guid sceneId,
        [FromQuery] bool force,
        [FromServices] AiContentFactory.Application.Generation.IClipCheckService clipCheck,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await clipCheck.CheckAsync(contentProjectId, sceneId, force, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
        catch (AiContentFactory.Application.Costs.BudgetExceededException ex)
        {
            return Problem(ex.Message, title: "AI budget exceeded", statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (AiContentFactory.Application.Providers.LlmQuotaExceededException)
        {
            return Problem("Gemini báo đã chạm hạn mức/trần chi phí - thử lại sau.", statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (HttpRequestException)
        {
            return Problem("Không gọi được AI để kiểm tra clip - thử lại sau.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    public record ImportStockVideoBody(string VideoId);

    /// <summary>
    /// Free (no AI). Downloads a Pexels video and imports it as this scene's
    /// clip - same validation as a Google Flow clip, no Flow credits booked.
    /// </summary>
    [HttpPost("scenes/{sceneId:guid}/stock-video")]
    public async Task<ActionResult<AiContentFactory.Application.Generation.FlowClipImportResult>> ImportStockVideo(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] ImportStockVideoBody body,
        [FromServices] AiContentFactory.Application.Generation.IStockFootageService stock,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await stock.ImportAsync(contentProjectId, sceneId, body.VideoId, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    public record SetShotSizeBody(string ShotSize);

    /// <summary>Hand-set the framing for one scene - leads the cinematography sentence of the video prompt.</summary>
    [HttpPut("scenes/{sceneId:guid}/shot-size")]
    public async Task<ActionResult<StoryboardResponse>> SetShotSize(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetShotSizeBody body,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Domain.Storyboards.ShotSize>(body.ShotSize, ignoreCase: true, out var shot) || !Enum.IsDefined(shot))
        {
            return Problem($"Unknown shot size '{body.ShotSize}'.", statusCode: 400);
        }

        try
        {
            return Ok(await _service.SetSceneShotSizeAsync(contentProjectId, sceneId, shot, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetCharacterOnScreenBody(bool? CharacterOnScreen);

    /// <summary>
    /// Hand-set whether a recurring character is visible in one scene (decides
    /// whether its reference image is attached). Null = back to automatic.
    /// </summary>
    [HttpPut("scenes/{sceneId:guid}/character-on-screen")]
    public async Task<ActionResult<StoryboardResponse>> SetCharacterOnScreen(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetCharacterOnScreenBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SetSceneCharacterOnScreenAsync(contentProjectId, sceneId, body.CharacterOnScreen, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetSkipGenerationBody(bool Skip);

    /// <summary>
    /// Toggle "this clip already has a video - skip Veo". Turning it off also
    /// drops any imported clip so the scene can be generated again.
    /// </summary>
    [HttpPut("scenes/{sceneId:guid}/skip-generation")]
    public async Task<ActionResult<StoryboardResponse>> SetSkipGeneration(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetSkipGenerationBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _flowClipImportService.SetSkipGenerationAsync(contentProjectId, sceneId, body.Skip, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    /// <summary>Ask the prompt agent for a suggested prompt for one scene (synchronous - one cheap LLM call).</summary>
    [HttpPost("scenes/{sceneId:guid}/prompt/suggest")]
    public async Task<ActionResult<StoryboardResponse>> SuggestScenePrompt(
        Guid contentProjectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.SuggestScenePromptAsync(contentProjectId, sceneId, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetScenePromptBody(string? GenerationPrompt, string? NegativePrompt);

    /// <summary>Save a hand-edited generation prompt for one scene. Empty = regenerate at build time.</summary>
    [HttpPut("scenes/{sceneId:guid}/prompt")]
    public async Task<ActionResult<StoryboardResponse>> SetScenePrompt(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetScenePromptBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.SetScenePromptTextAsync(contentProjectId, sceneId, body.GenerationPrompt, body.NegativePrompt, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    /// <summary>Stage 1: generate (or regenerate) this scene's Keyframe still image. A real, billable image-generation call.</summary>
    [HttpPost("scenes/{sceneId:guid}/keyframe")]
    public async Task<ActionResult<SceneResponse>> GenerateKeyframe(
        Guid contentProjectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _keyframeService.GenerateKeyframeAsync(contentProjectId, sceneId, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    /// <summary>Approves the scene's currently-generated Keyframe so it can anchor a video generation.</summary>
    [HttpPost("scenes/{sceneId:guid}/keyframe/approve")]
    public async Task<ActionResult<StoryboardResponse>> ApproveKeyframe(
        Guid contentProjectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.ApproveKeyframeAsync(contentProjectId, sceneId, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            // Same 404 mapping as every other MutateSceneAsync-based endpoint
            // above (SetVisualType/SetModelTier/SetCameraMovement/SetSkipGeneration/SetMotionPrompt).
            return Problem(ex.Message, statusCode: 404);
        }
    }

    public record SetMotionPromptBody(string? MotionPrompt);

    /// <summary>Saves a hand-edited motion prompt for animating the scene's approved Keyframe. Blank = compose a default at generation time.</summary>
    [HttpPut("scenes/{sceneId:guid}/motion-prompt")]
    public async Task<ActionResult<StoryboardResponse>> SetMotionPrompt(
        Guid contentProjectId,
        Guid sceneId,
        [FromBody] SetMotionPromptBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.SetMotionPromptAsync(contentProjectId, sceneId, body.MotionPrompt, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }

    /// <summary>Stage 2: animate the scene's APPROVED Keyframe into a video via the standard Veo image-to-video call, at the scene's own configured duration. A real, billable video-generation call.</summary>
    [HttpPost("scenes/{sceneId:guid}/keyframe/video")]
    public async Task<ActionResult<SceneResponse>> GenerateVideoFromKeyframe(
        Guid contentProjectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _keyframeService.GenerateVideoFromKeyframeAsync(contentProjectId, sceneId, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }

    /// <summary>
    /// Import a clip the user generated in Google Flow as this scene's visual.
    /// The clip is ffprobed (readable, non-zero duration, resolution/aspect),
    /// matched to the scene, its metadata stored, the scene marked complete, and
    /// the Flow credits it cost booked. A clip that fails a hard check is
    /// rejected (nothing is stored) with 422 + the reasons.
    /// </summary>
    [HttpPost("scenes/{sceneId:guid}/video")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult<FlowClipImportResult>> UploadSceneVideo(
        Guid contentProjectId,
        Guid sceneId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem("Chưa chọn file video.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _flowClipImportService.ImportAsync(contentProjectId, sceneId, file.FileName, stream, cancellationToken);
            return result.Accepted
                ? Ok(result)
                : UnprocessableEntity(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpDelete("scenes/{sceneId:guid}")]
    public async Task<ActionResult<StoryboardResponse>> RemoveScene(
        Guid contentProjectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.RemoveSceneAsync(contentProjectId, sceneId, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }
}
