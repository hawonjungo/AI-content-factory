using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

public interface IAssetGenerationDispatcher
{
    /// <summary>
    /// Runs whichever generation pipeline the mode selects.
    /// </summary>
    /// <param name="autoHook">Google Flow only - skip the user's clip plan and auto-write a hook script.</param>
    Task RunAsync(Guid contentProjectId, GenerationMode mode, bool autoHook = false, CancellationToken cancellationToken = default);
}

public class AssetGenerationDispatcher : IAssetGenerationDispatcher
{
    private readonly IAssetGenerationService _standard;
    private readonly IGoogleFlowAssetGenerationService _googleFlow;
    private readonly IContentProjectRepository _projectRepository;
    private readonly VideoGenerationOptions _options;

    public AssetGenerationDispatcher(
        IAssetGenerationService standard,
        IGoogleFlowAssetGenerationService googleFlow,
        IContentProjectRepository projectRepository,
        IOptions<VideoGenerationOptions> options)
    {
        _standard = standard;
        _googleFlow = googleFlow;
        _projectRepository = projectRepository;
        _options = options.Value;
    }

    public async Task RunAsync(Guid contentProjectId, GenerationMode mode, bool autoHook = false, CancellationToken cancellationToken = default)
    {
        if (mode != GenerationMode.GoogleFlow)
        {
            await _standard.RunAsync(contentProjectId, cancellationToken);
            return;
        }

        if (!_options.GoogleFlowEnabled)
        {
            throw new DomainException("Chế độ Google Flow đang bị tắt.");
        }

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        // The Google Flow pipeline writes its own script from a topic + audience
        // rather than the reviewed script, so derive those from the project the
        // same way the standard script agent would.
        var topic = FirstNonBlank(project.Topic, project.Title);
        var audience = FirstNonBlank(project.Niche, "khán giả video ngắn nói chung");

        await _googleFlow.RunAsync(contentProjectId, topic, audience, autoHook, cancellationToken);
    }

    private static string FirstNonBlank(string? primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();
}
