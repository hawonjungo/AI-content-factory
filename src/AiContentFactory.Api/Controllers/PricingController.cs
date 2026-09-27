using AiContentFactory.Application.Costs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// Per-action cost ESTIMATES in USD for the cost notes shown next to every
/// billable button, read from the "Pricing" configuration so the UI never
/// hard-codes a number that drifts from the backend's own budget tracking.
/// Planning figures only, not provider billing.
/// </summary>
/// <param name="FastVideoUsdPer8s">One 8-second Veo Fast clip.</param>
/// <param name="LiteVideoUsdPer8s">One 8-second Veo Lite clip.</param>
/// <param name="ImageUsd">One AI image (reference, keyframe or still scene).</param>
/// <param name="TtsUsdPer1000Chars">AI voice-over per 1,000 narration characters.</param>
/// <param name="TextCallUsd">One short text call (e.g. a scene prompt suggestion).</param>
/// <param name="LongTextCallUsd">One long text call (a script or QA review).</param>
/// <param name="ClipCheckUsd">One AI clip check (frames + references into a vision model).</param>
public record PricingResponse(
    decimal FastVideoUsdPer8s,
    decimal LiteVideoUsdPer8s,
    decimal ImageUsd,
    decimal TtsUsdPer1000Chars,
    decimal TextCallUsd,
    decimal LongTextCallUsd,
    decimal ClipCheckUsd);

[ApiController]
[Route("pricing")]
public class PricingController : ControllerBase
{
    private readonly PricingOptions _pricing;

    public PricingController(IOptions<PricingOptions> pricing)
    {
        _pricing = pricing.Value;
    }

    [HttpGet]
    public ActionResult<PricingResponse> Get() => Ok(new PricingResponse(
        _pricing.FastVideoUsdPerSecond * 8,
        _pricing.LiteVideoUsdPerSecond * 8,
        _pricing.ImageUsd,
        _pricing.TtsUsdPer1000Chars,
        _pricing.TextCallUsd,
        _pricing.LongTextCallUsd,
        _pricing.ClipCheckUsd));
}
