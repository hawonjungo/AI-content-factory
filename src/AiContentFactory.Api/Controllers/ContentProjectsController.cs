using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects")]
public class ContentProjectsController : ControllerBase
{
    private readonly IContentProjectService _service;
    private readonly IContentProjectRepository _projectRepository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<ContentProjectsController> _logger;

    public ContentProjectsController(IContentProjectService service, IContentProjectRepository projectRepository, IBackgroundJobClient backgroundJobClient, ILogger<ContentProjectsController> logger)
    {
        _service = service;
        _projectRepository = projectRepository;
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentProjectResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var projects = await _service.GetAllAsync(cancellationToken);
        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentProjectResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var project = await _service.GetByIdAsync(id, cancellationToken);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<ContentProjectResponse>> Create(
        [FromBody] CreateContentProjectRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created ContentProject {ContentProjectId} - {Title}", created.Id, created.Title);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContentProjectResponse>> Update(
        Guid id,
        [FromBody] UpdateContentProjectRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _service.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<ContentProjectResponse>> ChangeStatus(
        Guid id,
        [FromBody] ChangeContentProjectStatusRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _service.ChangeStatusAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPost("{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, CancellationToken cancellationToken)
    {
        var project = await ReserveJobAsync(id, "script", "Äang xáº¿p hÃ ng viáº¿t ká»‹ch báº£n", cancellationToken);
        if (project is null)
        {
            return await _service.GetByIdAsync(id, cancellationToken) is null
                ? NotFound()
                : Conflict("Dá»± Ã¡n nÃ y Ä‘ang cÃ³ má»™t tÃ¡c vá»¥ chÆ°a hoÃ n táº¥t. Vui lÃ²ng chÆ° má»™t lÃºc.");
        }

        var jobId = _backgroundJobClient.Enqueue<ContentPipelineJob>(job => job.RunAsync(id));
        _logger.LogInformation("Enqueued content pipeline job {JobId} for ContentProject {ContentProjectId}", jobId, id);

        return Accepted(new { jobId });
    }

    /// <param name="mode">
    /// "standard" (default) uses your clip plan with Veo text-to-video.
    /// "googleflow" runs the fixed ~20s hook pipeline (Nano Banana image ->
    /// Veo image-to-video, daily-quota limited) and ignores the clip plan.
    /// </param>
    [HttpPost("{id:guid}/generate-assets")]
    public async Task<IActionResult> GenerateAssets(Guid id, [FromQuery] string? mode, [FromQuery] bool autoHook, CancellationToken cancellationToken)
    {
        var project = await ReserveJobAsync(id, AssetGenerationService.ClipsStage, "Äang xáº¿p hÃ ng dá»±ng video", cancellationToken);
        if (project is null)
        {
            return await _service.GetByIdAsync(id, cancellationToken) is null
                ? NotFound()
                : Conflict("Dá»± Ã¡n nÃ y Ä‘ang cÃ³ má»™t tÃ¡c vá»¥ chÆ°a hoÃ n táº¥t. Vui lÃ²ng chÆ° má»™t lÃºc.");
        }

        var normalizedMode = GenerationModeParser.Parse(mode).ToString();
        var jobId = _backgroundJobClient.Enqueue<AssetGenerationJob>(job => job.RunAsync(id, normalizedMode, autoHook));
        _logger.LogInformation(
            "Enqueued asset generation job {JobId} for ContentProject {ContentProjectId} (mode={Mode}, autoHook={AutoHook})",
            jobId, id, normalizedMode, autoHook);

        return Accepted(new { jobId });
    }

    /// <summary>
    /// Captions are no longer a render argument - they come from the project's
    /// caption settings (PUT /content-projects/{id}/captions), so turning them
    /// off and re-rendering is a project-level change rather than a per-run flag.
    /// </summary>
    [HttpPost("{id:guid}/render")]
    public async Task<IActionResult> Render(Guid id, CancellationToken cancellationToken)
    {
        var project = await ReserveJobAsync(id, "render", "Äang xáº¿p hÃ ng ghÃ©p video", cancellationToken);
        if (project is null)
        {
            return await _service.GetByIdAsync(id, cancellationToken) is null
                ? NotFound()
                : Conflict("Video Ä‘ang Ä‘Æ°á»£c ghÃ©p hoáº·c má»™t tÃ¡c vá»¥ khÃ¡c Ä‘ang cháº¡y. Vui lÃ²ng chÆ° má»™t lÃºc.");
        }

        var jobId = _backgroundJobClient.Enqueue<RenderJob>(job => job.RunAsync(id));
        _logger.LogInformation("Enqueued render job {JobId} for ContentProject {ContentProjectId}", jobId, id);

        return Accepted(new { jobId });
    }

    [HttpPost("{id:guid}/run-qa")]
    public async Task<IActionResult> RunQa(Guid id, CancellationToken cancellationToken)
    {
        var project = await ReserveJobAsync(id, "qa", "Äang xáº¿p hÃ ng cháº¥m Ä‘iá»ƒm", cancellationToken);
        if (project is null)
        {
            return await _service.GetByIdAsync(id, cancellationToken) is null
                ? NotFound()
                : Conflict("Dá»± Ã¡n nÃ y Ä‘ang cÃ³ má»™t tÃ¡c vá»¥ chÆ°a hoÃ n táº¥t. Vui lÃ²ng chÆ° má»™t lÃºc.");
        }

        var jobId = _backgroundJobClient.Enqueue<QaJob>(job => job.RunAsync(id));
        _logger.LogInformation("Enqueued QA job {JobId} for ContentProject {ContentProjectId}", jobId, id);

        return Accepted(new { jobId });
    }

    private ActionResult ValidationProblem(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        return ValidationProblem(ModelState);
    }

    private async Task<ContentProject?> ReserveJobAsync(Guid id, string stage, string message, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAsync(id, cancellationToken);
        if (project is null || !project.Progress.IsIdle) return null;

        // Persist this before Hangfire receives the job. The UI sees `busy`
        // immediately, and duplicate clicks/API requests are rejected while
        // the job is still waiting in Hangfire's queue.
        project.ReportProgress(stage, 0, 1, message);
        await _projectRepository.SaveChangesAsync(cancellationToken);
        return project;
    }
}
