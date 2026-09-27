using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

/// <summary>Free stock footage search (Pexels). No AI, no cost - needs a free Pexels API key.</summary>
[ApiController]
[Route("stock")]
public class StockController : ControllerBase
{
    private readonly IStockFootageService _stock;

    public StockController(IStockFootageService stock)
    {
        _stock = stock;
    }

    [HttpGet("videos")]
    public async Task<ActionResult<IReadOnlyList<StockVideo>>> SearchVideos([FromQuery] string? query, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _stock.SearchAsync(query ?? string.Empty, page, cancellationToken));
        }
        catch (DomainException ex)
        {
            return Problem(ex.Message, statusCode: 400);
        }
    }
}
