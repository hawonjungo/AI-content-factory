using AiContentFactory.Application.Publishing;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Publishing;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

/// <summary>Step 7 publishing / scheduling for one content project.</summary>
[ApiController]
[Route("content-projects/{contentProjectId:guid}/publish")]
public class PublishingController : ControllerBase
{
    private readonly IPublishingService _publishing;
    private readonly ILogger<PublishingController> _logger;

    public PublishingController(IPublishingService publishing, ILogger<PublishingController> logger)
    {
        _publishing = publishing;
        _logger = logger;
    }

    /// <param name="PlatformPrivacy">
    /// Per-platform privacy/visibility choice, keyed by platform slug/name (e.g.
    /// { "tiktok": "SELF_ONLY", "youtube": "unlisted" }). Optional; a missing
    /// entry falls back to that platform's own default.
    /// </param>
    public record PublishBody(
        IReadOnlyList<string> Platforms,
        string Title,
        string? Caption,
        string? Hashtags,
        string Mode,
        DateTimeOffset? ScheduledAt,
        IReadOnlyDictionary<string, string>? PlatformPrivacy = null);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublishJobDto>>> GetJobs(Guid contentProjectId, CancellationToken cancellationToken) =>
        Ok(await _publishing.GetJobsAsync(contentProjectId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<PublishResponse>> Publish(
        Guid contentProjectId,
        [FromBody] PublishBody body,
        CancellationToken cancellationToken)
    {
        var platforms = new List<PublishTarget>();
        foreach (var raw in body.Platforms ?? Array.Empty<string>())
        {
            if (PublishTargets.TryParse(raw, out var target))
            {
                platforms.Add(target);
            }
            else
            {
                return Problem($"Unknown platform '{raw}'.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var mode = string.Equals(body.Mode, "schedule", StringComparison.OrdinalIgnoreCase)
            ? PublishMode.Schedule
            : PublishMode.Now;

        Dictionary<PublishTarget, string>? platformPrivacy = null;
        foreach (var (rawPlatform, privacy) in body.PlatformPrivacy ?? new Dictionary<string, string>())
        {
            if (!PublishTargets.TryParse(rawPlatform, out var target) || string.IsNullOrWhiteSpace(privacy))
            {
                continue;
            }
            (platformPrivacy ??= new Dictionary<PublishTarget, string>())[target] = privacy;
        }

        try
        {
            var result = await _publishing.PublishAsync(
                contentProjectId,
                new PublishRequest(platforms, body.Title, body.Caption, body.Hashtags, mode, body.ScheduledAt, platformPrivacy),
                cancellationToken);
            return Ok(result);
        }
        catch (PublishValidationException ex)
        {
            return UnprocessableEntity(new { errors = ex.Errors });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }

    /// <summary>
    /// Clears Failed/Cancelled history entries for this project. A successful
    /// publish (and its published link) is never deleted by this - it isn't
    /// "history clutter", it's the record of what actually got posted.
    /// </summary>
    [HttpDelete("history")]
    public async Task<ActionResult<IReadOnlyList<PublishJobDto>>> ClearHistory(Guid contentProjectId, CancellationToken cancellationToken) =>
        Ok(await _publishing.ClearHistoryAsync(contentProjectId, cancellationToken));

    [HttpPost("{publishJobId:guid}/retry")]
    public async Task<ActionResult<PublishJobDto>> Retry(Guid contentProjectId, Guid publishJobId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _publishing.RetryAsync(contentProjectId, publishJobId, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
