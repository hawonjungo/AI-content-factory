using AiContentFactory.Application.Presets;
using Microsoft.AspNetCore.Mvc;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// The preset catalog is static code, so this needs no database round-trip and
/// the frontend can fetch it once at wizard start.
/// </summary>
[ApiController]
[Route("presets")]
public class PresetsController : ControllerBase
{
    private readonly IPresetService _service;

    public PresetsController(IPresetService service)
    {
        _service = service;
    }

    [HttpGet]
    public ActionResult<PresetCatalogResponse> GetCatalog() => Ok(_service.GetCatalog());
}
