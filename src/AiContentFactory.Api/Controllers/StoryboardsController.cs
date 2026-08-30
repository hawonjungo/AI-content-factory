using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/storyboard")]
public class StoryboardsController : ControllerBase
{
    private readonly IStoryboardService _service;

    public StoryboardsController(IStoryboardService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<StoryboardResponse>> Get(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var storyboard = await _service.GetOrCreateAsync(contentProjectId, cancellationToken);
        return Ok(storyboard);
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
