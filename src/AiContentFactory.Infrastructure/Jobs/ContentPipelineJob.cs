using AiContentFactory.Application.Agents;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

/// <summary>
/// Thin Hangfire wrapper around IContentPipelineService. Kept parameter-free
/// beyond the project id (no CancellationToken in the signature) since
/// Hangfire serializes job arguments to storage and CancellationToken isn't
/// meant to survive that round-trip.
/// </summary>
public class ContentPipelineJob
{
    private readonly IContentPipelineService _pipelineService;

    public ContentPipelineJob(IContentPipelineService pipelineService)
    {
        _pipelineService = pipelineService;
    }

    [AutomaticRetry(Attempts = 0)] // Agents already retry internally; don't silently re-run a whole pipeline.
    public Task RunAsync(Guid contentProjectId) => _pipelineService.RunAsync(contentProjectId, CancellationToken.None);
}
