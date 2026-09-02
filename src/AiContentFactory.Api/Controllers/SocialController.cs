using AiContentFactory.Application.Publishing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// Platform connection + OAuth for Step 7. The callback finishes the OAuth
/// handshake server-side (client secrets never touch the browser) and then
/// redirects back to the wizard.
/// </summary>
[ApiController]
[Route("social")]
public class SocialController : ControllerBase
{
    private readonly ISocialConnectionService _connections;
    private readonly PublishingOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SocialController> _logger;

    public SocialController(
        ISocialConnectionService connections,
        IOptions<PublishingOptions> options,
        IConfiguration configuration,
        ILogger<SocialController> logger)
    {
        _connections = connections;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("connections")]
    public async Task<ActionResult<IReadOnlyList<SocialConnectionDto>>> GetConnections(CancellationToken cancellationToken) =>
        Ok(await _connections.GetStatusesAsync(cancellationToken));

    [HttpGet("connections/{platform}/authorize")]
    public async Task<IActionResult> Authorize(string platform, CancellationToken cancellationToken)
    {
        if (!PublishTargets.TryParse(platform, out var target))
        {
            return Problem($"Unknown platform '{platform}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var url = await _connections.BuildAuthorizationUrlAsync(target, redirectUriOverride: null, cancellationToken);
            return Ok(new { authorizationUrl = url });
        }
        catch (PublishException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    [HttpGet("connections/{platform}/callback")]
    public async Task<IActionResult> Callback(string platform, [FromQuery] string? code, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (!PublishTargets.TryParse(platform, out var target))
        {
            return Problem($"Unknown platform '{platform}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var returnUrl = ResolveReturnUrl();

        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogWarning("OAuth callback for {Platform} returned error: {Error}", target, error);
            return Redirect($"{returnUrl}?social_error={Uri.EscapeDataString(error!)}&platform={PublishTargets.Slug(target)}");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Redirect($"{returnUrl}?social_error=missing_code&platform={PublishTargets.Slug(target)}");
        }

        try
        {
            var result = await _connections.CompleteAsync(target, code!, redirectUriOverride: null, cancellationToken);
            var slug = PublishTargets.Slug(target);
            return result.Status == nameof(Domain.Publishing.SocialConnectionStatus.PendingSelection)
                ? Redirect($"{returnUrl}?social_select_page={slug}")
                : Redirect($"{returnUrl}?social_connected={slug}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to complete OAuth for {Platform}", target);
            return Redirect($"{returnUrl}?social_error={Uri.EscapeDataString(ex.Message)}&platform={PublishTargets.Slug(target)}");
        }
    }

    public record SelectPageBody(string PageId);

    /// <summary>Finalises a Facebook connection that is waiting on a Page choice.</summary>
    [HttpPost("connections/{platform}/page")]
    public async Task<ActionResult<SocialConnectionDto>> SelectPage(string platform, [FromBody] SelectPageBody body, CancellationToken cancellationToken)
    {
        if (!PublishTargets.TryParse(platform, out var target))
        {
            return Problem($"Unknown platform '{platform}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(body?.PageId))
        {
            return Problem("pageId is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return Ok(await _connections.SelectPageAsync(target, body.PageId, cancellationToken));
        }
        catch (PublishException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    [HttpDelete("connections/{platform}")]
    public async Task<IActionResult> Disconnect(string platform, CancellationToken cancellationToken)
    {
        if (!PublishTargets.TryParse(platform, out var target))
        {
            return Problem($"Unknown platform '{platform}'.", statusCode: StatusCodes.Status400BadRequest);
        }

        await _connections.DisconnectAsync(target, cancellationToken);
        return NoContent();
    }

    private string ResolveReturnUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.FrontendReturnUrl))
        {
            return _options.FrontendReturnUrl.TrimEnd('/');
        }

        var origin = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.FirstOrDefault();
        return string.IsNullOrWhiteSpace(origin) ? "/" : origin.TrimEnd('/');
    }
}
