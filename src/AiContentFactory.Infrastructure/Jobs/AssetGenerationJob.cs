using AiContentFactory.Application.Generation;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

public class AssetGenerationJob
{
    private readonly IAssetGenerationDispatcher _dispatcher;

    public AssetGenerationJob(IAssetGenerationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // mode is the persisted job argument, so the string form is what survives a
    // Hangfire restart - parse it here rather than passing the enum.
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId, string? mode, bool autoHook) =>
        _dispatcher.RunAsync(contentProjectId, GenerationModeParser.Parse(mode), autoHook, CancellationToken.None);
}
