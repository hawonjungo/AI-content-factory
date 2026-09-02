using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/assets")]
public class AssetsController : ControllerBase
{
    private readonly IAssetService _service;
    private readonly IFileStorage _fileStorage;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public AssetsController(IAssetService service, IFileStorage fileStorage)
    {
        _service = service;
        _fileStorage = fileStorage;
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

    [HttpGet("{assetId:guid}/file")]
    public async Task<IActionResult> DownloadFile(Guid contentProjectId, Guid assetId, CancellationToken cancellationToken)
    {
        var assets = await _service.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        var asset = assets.FirstOrDefault(a => a.Id == assetId);

        if (asset is null || string.IsNullOrWhiteSpace(asset.FilePath))
        {
            return NotFound();
        }

        if (!ContentTypeProvider.TryGetContentType(asset.FilePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = await _fileStorage.GetAsync(asset.FilePath, cancellationToken);
        return File(stream, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// Uploads a background music track. Replaces asking the user to type a
    /// server-side file path into the old "register asset manually" form.
    /// </summary>
    [HttpPost("music")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<AssetResponse>> UploadMusic(
        Guid contentProjectId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem("Chưa chọn file nhạc.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var asset = await _service.UploadMusicAsync(contentProjectId, file.FileName, stream, cancellationToken);
            return Ok(asset);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
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
