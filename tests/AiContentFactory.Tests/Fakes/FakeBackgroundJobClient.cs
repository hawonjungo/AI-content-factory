using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace AiContentFactory.Tests.Fakes;

/// <summary>
/// Records every job Hangfire's fluent <c>Enqueue&lt;T&gt;(...)</c> extension
/// methods resolve down to <see cref="IBackgroundJobClient.Create"/>, so
/// tests can assert "job X was enqueued for project Y" without a real
/// Hangfire storage backend.
/// </summary>
public sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> CreatedJobs { get; } = new();

    public string Create(Job job, IState state)
    {
        CreatedJobs.Add(job);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
