using AiContentFactory.Application.Qa;
using Hangfire;

namespace AiContentFactory.Infrastructure.Jobs;

public class QaJob
{
    private readonly IQaService _service;

    public QaJob(IQaService service)
    {
        _service = service;
    }

    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid contentProjectId) => _service.RunAsync(contentProjectId, CancellationToken.None);
}
