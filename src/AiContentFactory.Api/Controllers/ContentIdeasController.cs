using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Ideas;
using AiContentFactory.Application.Providers;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// Step 2 "AI Gợi ý nội dung": generates the single best idea-level content
/// suggestion for the chosen goal/niche. Not project-scoped - the wizard just
/// passes the current project's language/duration and (optionally) the user's
/// own ideas.
/// </summary>
[ApiController]
[Route("content-ideas")]
public class ContentIdeasController : ControllerBase
{
    private readonly IContentIdeasService _service;
    private readonly ILogger<ContentIdeasController> _logger;

    public ContentIdeasController(IContentIdeasService service, ILogger<ContentIdeasController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("suggestions")]
    public async Task<ActionResult<ContentIdeaSuggestionsResponse>> Suggest(
        [FromBody] ContentIdeaSuggestionsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.SuggestAsync(request, cancellationToken));
        }
        catch (LlmQuotaExceededException ex)
        {
            // Provider rejected the call on a quota / billing spend cap. Report it
            // as an AI budget limit so the user isn't left guessing.
            _logger.LogWarning(ex, "AI content idea suggestions blocked - LLM quota / budget limit reached");
            return Problem(
                "Đã đạt giới hạn chi phí / credit AI. Vui lòng thử lại sau, hoặc kiểm tra hạn mức (spend cap) của API key.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (AgentGenerationException ex)
        {
            // The model failed to return usable JSON even after one repair pass.
            // Surface a clean, retryable error rather than a 500.
            _logger.LogWarning(ex, "AI content idea suggestions could not be generated");
            return Problem(
                "Không thể tạo gợi ý lúc này. Vui lòng thử lại.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
