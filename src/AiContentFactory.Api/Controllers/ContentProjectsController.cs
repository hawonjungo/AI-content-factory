using AiContentFactory.Application.ContentProjects;
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
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<ContentProjectsController> _logger;

    public ContentProjectsController(IContentProjectService service, IBackgroundJobClient backgroundJobClient, ILogger<ContentProjectsController> logger)
    {
        _service = service;
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
        var project = await _service.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        if (project.Status == nameof(Domain.ContentProjects.ContentProjectStatus.Failed))
        {
            await _service.ChangeStatusAsync(
                id,
                new ChangeContentProjectStatusRequest(Domain.ContentProjects.ContentProjectStatus.Draft),
                cancellationToken);
        }

        var jobId = _backgroundJobClient.Enqueue<ContentPipelineJob>(job => job.RunAsync(id));
        _logger.LogInformation("Enqueued content pipeline job {JobId} for ContentProject {ContentProjectId}", jobId, id);

        return Accepted(new { jobId });
    }

    private ActionResult ValidationProblem(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        return ValidationProblem(ModelState);
    }
}
