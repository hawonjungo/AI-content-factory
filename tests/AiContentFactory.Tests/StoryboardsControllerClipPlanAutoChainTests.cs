using AiContentFactory.Api.Controllers;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Infrastructure.Jobs;
using AiContentFactory.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="StoryboardsController.GenerateClipPlan"/>'s auto-chain of
/// <see cref="SuggestAllScenePromptsJob"/> - "Chia clip" replaces the
/// storyboard's scenes (and any prompts they had) every time it runs, so the
/// bulk prompt-suggestion job should be enqueued automatically after every
/// successful clip-plan generation, without the user having to click the
/// manual "Tạo gợi ý cho tất cả cảnh" button separately.
/// </summary>
public class StoryboardsControllerClipPlanAutoChainTests
{
    /// <summary>Always succeeds; ignores the request and returns an empty storyboard for the project.</summary>
    private sealed class FakeClipPlanService : IClipPlanService
    {
        public int Calls { get; private set; }

        public Task<StoryboardResponse> GenerateAsync(Guid contentProjectId, GenerateClipPlanRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new StoryboardResponse(Guid.NewGuid(), contentProjectId, Array.Empty<SceneResponse>()));
        }
    }

    private sealed class NotSupportedFlowClipImportService : IFlowClipImportService
    {
        public Task<FlowClipImportResult> ImportExternalAsync(Guid c, Guid s, string f, Stream st, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FlowClipImportResult> ImportAsync(Guid contentProjectId, Guid sceneId, string fileName, Stream content, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FlowImportStatus> GetStatusAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StoryboardResponse> SetSkipGenerationAsync(Guid contentProjectId, Guid sceneId, bool skip, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NotSupportedSceneKeyframeService : ISceneKeyframeService
    {
        public Task<SceneResponse> GenerateKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SceneResponse> GenerateVideoFromKeyframeAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static (StoryboardsController Controller, FakeClipPlanService ClipPlanService, FakeBackgroundJobClient JobClient, FakeContentProjectRepository ProjectRepo)
        Build(ContentProject project)
    {
        var storyboardRepo = new FakeStoryboardRepository(Storyboard.Create(project.Id));
        var storyboardService = new FakeStoryboardService(storyboardRepo);
        var clipPlanService = new FakeClipPlanService();
        var flowClipImportService = new NotSupportedFlowClipImportService();
        var projectRepo = new FakeContentProjectRepository(project);
        var jobReservation = new ProjectJobReservationService(projectRepo);
        var jobClient = new FakeBackgroundJobClient();

        var controller = new StoryboardsController(
            storyboardService,
            clipPlanService,
            flowClipImportService,
            new NotSupportedSceneKeyframeService(),
            jobReservation,
            jobClient,
            NullLogger<StoryboardsController>.Instance);

        return (controller, clipPlanService, jobClient, projectRepo);
    }

    private static GenerateClipPlanRequest Request() => new(ClipCount: 3, ClipDurationSeconds: 8);

    /// <summary>
    /// GenerateClipPlan returns via `Ok(result)` (an ActionResult, implicitly
    /// converted to ActionResult&lt;StoryboardResponse&gt;), so the payload
    /// lands in `.Result` as an OkObjectResult, not in `.Value` - only a bare
    /// `return result;` would populate `.Value` directly.
    /// </summary>
    private static StoryboardResponse AssertOk(Microsoft.AspNetCore.Mvc.ActionResult<StoryboardResponse> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<StoryboardResponse>(ok.Value);
    }

    [Fact]
    public async Task Successful_clip_plan_generation_auto_enqueues_the_bulk_prompt_job()
    {
        var project = ContentProject.Create("Auto-chain test", "topic", "niche", 60, "9:16", "en");
        var (controller, clipPlanService, jobClient, _) = Build(project);

        var result = await controller.GenerateClipPlan(project.Id, Request(), CancellationToken.None);

        Assert.Equal(1, clipPlanService.Calls);
        AssertOk(result);

        var job = Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(typeof(SuggestAllScenePromptsJob), job.Type);
        Assert.Equal(nameof(SuggestAllScenePromptsJob.RunAsync), job.Method.Name);
        Assert.Equal(project.Id, Assert.IsType<Guid>(job.Args[0]));

        // Progress must reflect the auto-chained job's stage, matching what
        // the manual "Tạo gợi ý cho tất cả cảnh" button reserves.
        Assert.Equal(StoryboardService.PromptsStage, project.Progress.Stage);
    }

    [Fact]
    public async Task Re_running_clip_plan_enqueues_the_job_again_each_time()
    {
        var project = ContentProject.Create("Re-run test", "topic", "niche", 60, "9:16", "en");
        var (controller, clipPlanService, jobClient, _) = Build(project);

        await controller.GenerateClipPlan(project.Id, Request(), CancellationToken.None);

        // The reservation from the first run must not still be "busy" once
        // that run's own flow finished, otherwise a real re-run (after the
        // background job completes and releases the lock) would always be
        // skipped - simulate the lock having been released, as
        // SuggestAllScenePromptsAsync itself always does on completion.
        project.ReportProgress(GenerationProgress.IdleStage, 0, 0, null);

        await controller.GenerateClipPlan(project.Id, Request(), CancellationToken.None);

        Assert.Equal(2, clipPlanService.Calls);
        Assert.Equal(2, jobClient.CreatedJobs.Count);
        Assert.All(jobClient.CreatedJobs, job =>
        {
            Assert.Equal(typeof(SuggestAllScenePromptsJob), job.Type);
            Assert.Equal(project.Id, Assert.IsType<Guid>(job.Args[0]));
        });
    }

    [Fact]
    public async Task Clip_plan_response_still_returns_promptly_and_the_job_enqueue_is_not_awaited_for_completion()
    {
        var project = ContentProject.Create("Fire-and-forget test", "topic", "niche", 60, "9:16", "en");
        var (controller, _, jobClient, _) = Build(project);

        var result = await controller.GenerateClipPlan(project.Id, Request(), CancellationToken.None);

        // The controller only awaits the (fast) reservation + Hangfire
        // Create() call, never the job's own execution - FakeBackgroundJobClient
        // never runs SuggestAllScenePromptsJob.RunAsync itself, so by the
        // time GenerateClipPlan's response comes back the job is merely
        // recorded as created, proving the HTTP response isn't gated on the
        // job finishing.
        AssertOk(result);
        Assert.Single(jobClient.CreatedJobs);
    }

    [Fact]
    public async Task Clip_plan_still_succeeds_when_the_project_is_already_busy_with_another_job()
    {
        var project = ContentProject.Create("Busy-skip test", "topic", "niche", 60, "9:16", "en");
        var (controller, clipPlanService, jobClient, _) = Build(project);

        // Simulate another job already holding the busy lock (e.g. a render
        // or asset-generation job in flight) when "Chia clip" is called.
        project.ReportProgress("render", 0, 1, "Đang ghép video");

        var result = await controller.GenerateClipPlan(project.Id, Request(), CancellationToken.None);

        // Clip-plan generation itself is unaffected by the busy lock (it has
        // no gate of its own) and must still return its own result...
        Assert.Equal(1, clipPlanService.Calls);
        AssertOk(result);

        // ...but the auto-chain must be skipped silently rather than
        // overwriting the in-flight job's progress or throwing.
        Assert.Empty(jobClient.CreatedJobs);
        Assert.Equal("render", project.Progress.Stage);
    }
}
