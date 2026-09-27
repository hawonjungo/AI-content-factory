using AiContentFactory.Application.Storyboards;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

/// <summary>
/// Bulk-suggests a prompt for every scene in the project's storyboard that
/// doesn't have one yet. Runs as a background job rather than a synchronous
/// HTTP call because it can make one billable PromptAgent call per
/// unprompted scene, which can take a couple of minutes for a project with
/// many scenes.
/// </summary>
public class SuggestAllScenePromptsJob
{
    private readonly IStoryboardService _service;

    public SuggestAllScenePromptsJob(IStoryboardService service)
    {
        _service = service;
    }

    // Never retried automatically: SuggestAllScenePromptsAsync already skips
    // scenes that succeeded on an earlier attempt, but a whole-job retry
    // would still resend a fresh, in-flight billable PromptAgent call for
    // whichever scene the previous attempt was on when it failed. Same rule
    // as every other job in this project - see AssetGenerationJob.
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId) =>
        _service.SuggestAllScenePromptsAsync(contentProjectId, CancellationToken.None);

    /// <summary>The "re-suggest every scene" variant - see <see cref="IStoryboardService.SuggestAllScenePromptsAsync(Guid, bool, CancellationToken)"/>.</summary>
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId, bool includePrompted) =>
        _service.SuggestAllScenePromptsAsync(contentProjectId, includePrompted, CancellationToken.None);
}
