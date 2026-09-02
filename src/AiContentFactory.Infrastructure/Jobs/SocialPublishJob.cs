using AiContentFactory.Application.Publishing;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

/// <summary>
/// Runs one (video → one platform) publish. Retry policy is deliberately split:
/// <see cref="PublishingService"/> decides permanent-vs-transient and only
/// rethrows for transient failures under the attempt cap; Hangfire then retries
/// those a few times with backoff. A permanent failure is recorded and the job
/// method returns normally, so Hangfire does not retry it.
/// </summary>
public class SocialPublishJob
{
    private readonly IPublishingService _service;

    public SocialPublishJob(IPublishingService service)
    {
        _service = service;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task RunAsync(Guid publishJobId) =>
        _service.RunJobAsync(publishJobId, CancellationToken.None);
}
