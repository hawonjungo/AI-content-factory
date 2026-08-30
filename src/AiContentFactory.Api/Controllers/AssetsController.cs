using AiContentFactory.Application.Assets;
using AiContentFactory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/assets")]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _service;

    public AssetsController(IAssetService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AssetResponse>>> GetAll(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var assets = await _service.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        return Ok(assets);
    }

    [HttpPost]
    public async Task<ActionResult<AssetResponse>> Create(
        Guid contentProjectId,
        [FromBody] CreateAssetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(contentProjectId, request, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{assetId:guid}")]
    public async Task<IActionResult> Delete(Guid contentProjectId, Guid assetId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(contentProjectId, assetId, cancellationToken);
            return NoContent();
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 404);
        }
    }
}
