using AiContentFactory.Application.Generation;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

public class ClipRegenerationJob
{
    private readonly IClipRegenerationService _service;

    public ClipRegenerationJob(IClipRegenerationService service)
    {
        _service = service;
    }

    // No automatic retry, same as the other generation jobs: a retry would
    // silently spend another clip's worth of Veo credits.
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId, Guid sceneId, string? narrationOverride, bool regenerateVoice) =>
        _service.RunAsync(
            contentProjectId,
            sceneId,
            new RegenerateClipRequest(narrationOverride, regenerateVoice),
            CancellationToken.None);
}
