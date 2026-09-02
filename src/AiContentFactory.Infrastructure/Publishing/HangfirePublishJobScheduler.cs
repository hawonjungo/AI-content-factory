using AiContentFactory.Application.Publishing;
using AiContentFactory.Infrastructure.Jobs;
using Hangfire;

namespace AiContentFactory.Infrastructure.Publishing;

/// <summary>The only place Application-side publishing touches Hangfire: enqueue-now vs schedule-at.</summary>
public class HangfirePublishJobScheduler : IPublishJobScheduler
{
    private readonly IBackgroundJobClient _client;

    public HangfirePublishJobScheduler(IBackgroundJobClient client)
    {
        _client = client;
    }

    public void EnqueueNow(Guid publishJobId) =>
        _client.Enqueue<SocialPublishJob>(job => job.RunAsync(publishJobId));

    public void Schedule(Guid publishJobId, DateTimeOffset runAtUtc) =>
        _client.Schedule<SocialPublishJob>(job => job.RunAsync(publishJobId), runAtUtc);
}
