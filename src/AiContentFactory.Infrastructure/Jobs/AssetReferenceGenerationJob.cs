using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Domain.AssetReferences;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

public class AssetReferenceGenerationJob
{
    private readonly IAssetReferenceGenerationService _service;

    public AssetReferenceGenerationJob(IAssetReferenceGenerationService service)
    {
        _service = service;
    }

    // No retry: a retry would silently spend another few image calls.
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId, string type, int count, string? prompt)
    {
        var parsed = Enum.TryParse<AssetReferenceType>(type, ignoreCase: true, out var t) ? t : AssetReferenceType.Character;
        return _service.GenerateAsync(contentProjectId, parsed, count, prompt, CancellationToken.None);
    }
}
