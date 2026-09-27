using System.Reflection;
using AiContentFactory.Infrastructure.Jobs;
using Hangfire;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Confirms <see cref="SuggestAllScenePromptsJob"/> follows this project's
/// "never let Hangfire retry a job that makes billable AI calls" rule (see
/// AssetGenerationJob/QaJob/RenderJob) - a whole-job retry would resend a
/// fresh, in-flight PromptAgent call rather than safely resuming.
/// </summary>
public class SuggestAllScenePromptsJobTests
{
    [Fact]
    public void RunAsync_disables_Hangfire_automatic_retry()
    {
        // Every overload (the default "unprompted only" run and the "re-suggest every scene" run).
        var methods = typeof(SuggestAllScenePromptsJob).GetMethods()
            .Where(m => m.Name == nameof(SuggestAllScenePromptsJob.RunAsync))
            .ToList();
        Assert.Equal(2, methods.Count);

        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<AutomaticRetryAttribute>();

            Assert.NotNull(attribute);
            Assert.Equal(0, attribute!.Attempts);
        }
    }
}
