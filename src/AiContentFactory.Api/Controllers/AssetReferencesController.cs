using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// The "Asset Reference" wizard step: Character + Environment reference images
/// the user locks in before any clip is generated. Kept off the wizard's
/// /overview endpoint's no-leak rules only for the file stream; everything
/// else returns friendly slot DTOs.
/// </summary>
[ApiController]
[Route("content-projects/{contentProjectId:guid}/asset-references")]
public class AssetReferencesController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly IAssetReferenceService _service;
    private readonly IAssetReferenceGenerationService _generationService;
    private readonly IContentProjectRepository _projectRepository;
    private readonly IAssetReferenceRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly PricingOptions _pricing;
    private readonly ILogger<AssetReferencesController> _logger;

    public AssetReferencesController(
        IAssetReferenceService service,
        IAssetReferenceGenerationService generationService,
        IContentProjectRepository projectRepository,
        IAssetReferenceRepository repository,
        IFileStorage fileStorage,
        IBackgroundJobClient backgroundJobClient,
        IOptions<PricingOptions> pricing,
        ILogger<AssetReferencesController> logger)
    {
        _service = service;
        _generationService = generationService;
        _projectRepository = projectRepository;
        _repository = repository;
        _fileStorage = fileStorage;
        _backgroundJobClient = backgroundJobClient;
        _pricing = pricing.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<AssetReferenceSlotsDto>> GetSlots(Guid contentProjectId, CancellationToken cancellationToken) =>
        Ok(await _service.GetSlotsAsync(contentProjectId, cancellationToken));

    /// <summary>
    /// The FULL list of named reference rows for a project (every (Type,
    /// Label) row, including legacy null-Label ones) - unlike <see cref="GetSlots"/>'s
    /// fixed Character+Environment pair. Used by the Story-linked
    /// multi-named-reference flow; purely additive, does not affect
    /// <see cref="GetSlots"/>.
    /// </summary>
    [HttpGet("named")]
    public async Task<ActionResult<IReadOnlyList<NamedAssetReferenceDto>>> GetNamedReferences(Guid contentProjectId, CancellationToken cancellationToken) =>
        Ok(await _service.GetNamedReferencesAsync(contentProjectId, cancellationToken));

    /// <summary>
    /// The default prompt this project would use for a reference type, for the
    /// human-in-the-loop review/edit before generation. The client sends the
    /// (possibly edited) prompt back to <c>POST generate</c>.
    ///
    /// Routed as <c>{type}/prompt</c> (a type segment, mirroring
    /// <c>{type}/skip</c>) rather than a bare literal <c>prompt</c>, which
    /// otherwise collides with the <c>{refId:guid}</c> single-segment routes and
    /// resolves to 405.
    /// </summary>
    [HttpGet("{type}/prompt")]
    public async Task<ActionResult<SuggestedReferencePromptResponse>> GetSuggestedPrompt(
        Guid contentProjectId,
        string type,
        CancellationToken cancellationToken)
    {
        if (!TryParseType(type, out var parsedType))
        {
            return Problem($"Loại ảnh mẫu không hợp lệ: '{type}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var suggested = await _generationService.BuildSuggestedPromptAsync(contentProjectId, parsedType, cancellationToken);
            return Ok(new SuggestedReferencePromptResponse(suggested.Prompt, suggested.NegativePrompt, _pricing.ImageUsd));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(Guid contentProjectId, [FromBody] GenerateAssetReferenceRequest request, CancellationToken cancellationToken)
    {
        if (!TryParseType(request.Type, out var type))
        {
            return Problem($"Loại ảnh mẫu không hợp lệ: '{request.Type}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var prompt = request.Prompt;
        var count = request.Count;

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
        if (project is null) return NotFound();
        if (!project.Progress.IsIdle)
        {
            return Conflict("Dá»± Ã¡n nÃ y Ä‘ang cÃ³ má»™t tÃ¡c vá»¥ chÆ°a hoÃ n táº¥t. Vui lÃ²ng chÆ° má»™t lÃºc.");
        }

        project.ReportProgress(AssetGenerationService.ReferencesStage, 0, count, "Äang xáº¿p hÃ ng táº¡o áº£nh máº«u");
        await _projectRepository.SaveChangesAsync(cancellationToken);
        var jobId = _backgroundJobClient.Enqueue<AssetReferenceGenerationJob>(job =>
            job.RunAsync(contentProjectId, type.ToString(), count, prompt));
        _logger.LogInformation("Enqueued asset reference job {JobId} for {ContentProjectId} ({Type})", jobId, contentProjectId, type);

        return Accepted();
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<AssetReferenceResponse>> Upload(
        Guid contentProjectId,
        [FromForm] string type,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryParseType(type, out var parsedType))
        {
            return Problem($"Loại ảnh mẫu không hợp lệ: '{type}'.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (file is null || file.Length == 0)
        {
            return Problem("Chưa chọn file ảnh.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _service.UploadAsync(contentProjectId, parsedType, file.FileName, stream, cancellationToken);
            return Ok(result);
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPut("{refId:guid}/approve")]
    public async Task<ActionResult<AssetReferenceSlotsDto>> Approve(Guid contentProjectId, Guid refId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ApproveAsync(contentProjectId, refId, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPut("{type}/skip")]
    public async Task<ActionResult<AssetReferenceSlotsDto>> Skip(Guid contentProjectId, string type, CancellationToken cancellationToken)
    {
        if (!TryParseType(type, out var parsedType))
        {
            return Problem($"Loại ảnh mẫu không hợp lệ: '{type}'.", statusCode: StatusCodes.Status400BadRequest);
        }
        return Ok(await _service.SkipAsync(contentProjectId, parsedType, cancellationToken));
    }

    [HttpDelete("{refId:guid}")]
    public async Task<IActionResult> Delete(Guid contentProjectId, Guid refId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteVariantAsync(contentProjectId, refId, cancellationToken);
            return NoContent();
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    [HttpGet("{refId:guid}/file")]
    public async Task<IActionResult> DownloadFile(Guid contentProjectId, Guid refId, CancellationToken cancellationToken)
    {
        var reference = await _repository.GetByIdAsync(refId, cancellationToken);
        if (reference is null || reference.ContentProjectId != contentProjectId || string.IsNullOrWhiteSpace(reference.ImagePath))
        {
            return NotFound();
        }

        if (!ContentTypeProvider.TryGetContentType(reference.ImagePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = await _fileStorage.GetAsync(reference.ImagePath, cancellationToken);
        return File(stream, contentType);
    }

    private static bool TryParseType(string? value, out AssetReferenceType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type);
}
