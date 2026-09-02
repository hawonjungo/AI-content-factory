using AiContentFactory.Application.Qa;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

[ApiController]
[Route("content-projects/{contentProjectId:guid}/qa-scores")]
public class QaScoresController : ControllerBase
{
    private readonly IQaService _qaService;

    public QaScoresController(IQaService qaService)
    {
        _qaService = qaService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QaScoreResponse>>> GetHistory(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var scores = await _qaService.GetHistoryAsync(contentProjectId, cancellationToken);
        return Ok(scores);
    }
}
