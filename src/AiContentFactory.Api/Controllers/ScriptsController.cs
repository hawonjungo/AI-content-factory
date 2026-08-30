using AiContentFactory.Application.Scripts;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/script")]
public class ScriptsController : ControllerBase
{
    private readonly IScriptService _service;

    public ScriptsController(IScriptService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ScriptResponse>> Get(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var script = await _service.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        return script is null ? NoContent() : Ok(script);
    }

    [HttpPut]
    public async Task<ActionResult<ScriptResponse>> Upsert(
        Guid contentProjectId,
        [FromBody] UpsertScriptRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpsertAsync(contentProjectId, request, cancellationToken);
        return Ok(result);
    }
}
