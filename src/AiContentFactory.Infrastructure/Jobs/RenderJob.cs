using AiContentFactory.Application.Rendering;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

public class RenderJob
{
    private readonly IRenderService _service;

    public RenderJob(IRenderService service)
    {
        _service = service;
    }

    // Whether captions are burned in is now part of the project's caption
    // settings, not a per-render argument.
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId) =>
        _service.RunAsync(contentProjectId, new RenderProjectRequest(), CancellationToken.None);
}
