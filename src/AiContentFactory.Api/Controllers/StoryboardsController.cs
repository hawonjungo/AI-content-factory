using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/storyboard")]
public class StoryboardsController : ControllerBase
{
    private readonly IStoryboardService _service;
    private readonly IClipPlanService _clipPlanService;
    private readonly IFlowClipImportService _flowClipImportService;

    public StoryboardsController(IStoryboardService service, IClipPlanService clipPlanService, IFlowClipImportService flowClipImportService)
    {
        _service = service;
        _clipPlanService = clipPlanService;
        _flowClipImportService = flowClipImportService;
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
