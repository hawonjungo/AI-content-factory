using System.Text;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Publishing;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class PublishingServiceTests
{
    private static readonly PublishTarget[] AllThree =
        { PublishTarget.TikTok, PublishTarget.YouTubeShorts, PublishTarget.InstagramReels };

    private static readonly PublishTarget[] AllPlatforms =
        { PublishTarget.TikTok, PublishTarget.YouTubeShorts, PublishTarget.InstagramReels, PublishTarget.FacebookPage };

    private sealed class Harness
    {
        public required PublishingService Service { get; init; }
        public required FakePublishJobRepository Jobs { get; init; }
        public required FakePublishJobScheduler Scheduler { get; init; }
        public required FakeSocialConnectionService Connections { get; init; }
        public required Dictionary<PublishTarget, FakeSocialPlatformPublisher> Publishers { get; init; }
        public required Guid ProjectId { get; init; }
        public required Guid VideoAssetId { get; init; }
    }

    private static async Task<Harness> BuildAsync(
        MediaInfo? probe = null,
        bool withFinalVideo = true,
        bool connectAll = true)
    {
        var project = ContentProject.Create("Office Cat", "cats", "storytelling", 60, "9:16", "en");
        var projectRepo = new FakeContentProjectRepository(project);
        var assets = new FakeAssetService();
        var storage = new FakeFileStorage();
        var jobs = new FakePublishJobRepository();
        var scheduler = new FakePublishJobScheduler();
        var connections = new FakeSocialConnectionService();

        var publishers = AllPlatforms.ToDictionary(p => p, p => new FakeSocialPlatformPublisher(p));
        if (connectAll)
        {
            foreach (var p in AllPlatforms)
            {
                connections.Connect(p);
            }
        }

        Guid videoId = Guid.Empty;
        if (withFinalVideo)
        {
            await storage.SaveAsync($"content-projects/{project.Id}/final.mp4", Encoding.ASCII.GetBytes(new string('x', 4096)));
            var asset = await assets.CreateAsync(project.Id, new CreateAssetRequest(
                SceneId: null, Type: AssetType.Video, Provider: "ffmpeg", Prompt: null,
                FilePath: $"content-projects/{project.Id}/final.mp4", DurationSeconds: 42, Width: 1080, Height: 1920));
            videoId = asset.Id;
        }

        var service = new PublishingService(
            projectRepo,
            assets,
            storage,
            new FakeMediaProbe(probe ?? FakeMediaProbe.GoodInfo(42, 1080, 1920)),
            jobs,
            connections,
            scheduler,
            publishers.Values,
            Options.Create(new PublishingOptions { PublicBaseUrl = "https://api.test" }),
            NullLogger<PublishingService>.Instance);

        return new Harness
        {
            Service = service,
            Jobs = jobs,
            Scheduler = scheduler,
            Connections = connections,
            Publishers = publishers,
            ProjectId = project.Id,
            VideoAssetId = videoId
        };
    }

    private static PublishRequest Now(params PublishTarget[] platforms) =>
        new(platforms, "My Video", "a caption", "#ai #cats", PublishMode.Now, null);

    // ---- validation --------------------------------------------------------

    [Fact]
    public async Task Publish_rejects_a_landscape_video_before_creating_any_job()
    {
        var h = await BuildAsync(probe: FakeMediaProbe.GoodInfo(42, 1920, 1080));

        var ex = await Assert.ThrowsAsync<PublishValidationException>(
            () => h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok)));

        Assert.Contains(ex.Errors, e => e.Contains("9:16"));
        Assert.Empty(h.Jobs.Jobs);
        Assert.Empty(h.Scheduler.EnqueuedNow);
    }

    [Fact]
    public async Task Publish_rejects_when_there_is_no_rendered_video()
    {
        var h = await BuildAsync(withFinalVideo: false);

        var ex = await Assert.ThrowsAsync<PublishValidationException>(
            () => h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok)));

        Assert.Contains(ex.Errors, e => e.Contains("video hoàn chỉnh"));
    }

    [Fact]
    public async Task Publish_skips_a_platform_whose_provider_video_constraint_fails_without_failing_the_others()
    {
        var h = await BuildAsync();
        h.Publishers[PublishTarget.TikTok].Constraint = (_, _) => VideoConstraintResult.Invalid("TikTok: clip quá dài.");

        var result = await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok, PublishTarget.YouTubeShorts));

        Assert.Single(result.Created);
        Assert.Equal(nameof(PublishTarget.YouTubeShorts), result.Created[0].Platform);
        Assert.Contains(result.Skipped, s => s.Platform == nameof(PublishTarget.TikTok) && s.Reason.Contains("quá dài"));
    }

    // ---- scheduling ------------------------------------------------------

    [Fact]
    public async Task Schedule_persists_the_time_and_schedules_the_job_instead_of_enqueuing_it()
    {
        var h = await BuildAsync();
        var at = DateTimeOffset.UtcNow.AddHours(2);

        var result = await h.Service.PublishAsync(h.ProjectId,
            new PublishRequest(new[] { PublishTarget.TikTok }, "T", null, null, PublishMode.Schedule, at));

        var job = Assert.Single(h.Jobs.Jobs);
        Assert.Equal(PublishJobStatus.Scheduled, job.Status);
        Assert.Equal(at, job.ScheduledAtUtc);
        Assert.Empty(h.Scheduler.EnqueuedNow);
        var scheduled = Assert.Single(h.Scheduler.Scheduled);
        Assert.Equal(job.Id, scheduled.Id);
        Assert.Equal(at, scheduled.At);
        Assert.Single(result.Created);
    }

    [Fact]
    public async Task Schedule_in_the_past_is_rejected()
    {
        var h = await BuildAsync();

        await Assert.ThrowsAsync<PublishValidationException>(() => h.Service.PublishAsync(h.ProjectId,
            new PublishRequest(new[] { PublishTarget.TikTok }, "T", null, null, PublishMode.Schedule, DateTimeOffset.UtcNow.AddMinutes(-5))));
    }

    [Fact]
    public async Task Publish_now_enqueues_one_independent_job_per_platform()
    {
        var h = await BuildAsync();

        var result = await h.Service.PublishAsync(h.ProjectId, Now(AllThree));

        Assert.Equal(3, result.Created.Count);
        Assert.Equal(3, h.Jobs.Jobs.Count);
        Assert.Equal(3, h.Scheduler.EnqueuedNow.Count);
        Assert.All(h.Jobs.Jobs, j => Assert.Equal(PublishJobStatus.Pending, j.Status));
    }

    // ---- provider failure isolation -------------------------------------

    [Fact]
    public async Task One_provider_failure_does_not_affect_the_other_platforms()
    {
        var h = await BuildAsync();
        h.Publishers[PublishTarget.TikTok].Upload = _ => throw new PublishException("TikTok từ chối", retryable: false);

        var result = await h.Service.PublishAsync(h.ProjectId, Now(AllThree));
        foreach (var job in h.Jobs.Jobs)
        {
            await h.Service.RunJobAsync(job.Id);
        }

        var byPlatform = h.Jobs.Jobs.ToDictionary(j => j.Platform);
        Assert.Equal(PublishJobStatus.Failed, byPlatform[PublishTarget.TikTok].Status);
        Assert.True(byPlatform[PublishTarget.TikTok].IsPermanentFailure);
        Assert.Equal(PublishJobStatus.Published, byPlatform[PublishTarget.YouTubeShorts].Status);
        Assert.Equal(PublishJobStatus.Published, byPlatform[PublishTarget.InstagramReels].Status);
        Assert.Equal("ext-id", byPlatform[PublishTarget.YouTubeShorts].ExternalPostId);
    }

    // ---- duplicate prevention -----------------------------------------

    [Fact]
    public async Task A_second_publish_for_the_same_project_and_platform_is_skipped_while_one_is_active()
    {
        var h = await BuildAsync();

        await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));
        var second = await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));

        Assert.Empty(second.Created);
        Assert.Contains(second.Skipped, s => s.Platform == nameof(PublishTarget.TikTok));
        Assert.Single(h.Jobs.Jobs); // still just the one job
    }

    [Fact]
    public async Task RunJob_is_idempotent_once_the_job_is_published()
    {
        var h = await BuildAsync();
        await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));
        var job = h.Jobs.Jobs.Single();

        await h.Service.RunJobAsync(job.Id);
        await h.Service.RunJobAsync(job.Id); // e.g. a stray Hangfire retry

        Assert.Equal(PublishJobStatus.Published, job.Status);
        Assert.Equal(1, h.Publishers[PublishTarget.TikTok].UploadCalls);
    }

    // ---- retry policy ------------------------------------------------

    [Fact]
    public async Task A_permanent_failure_is_recorded_and_not_rethrown_for_retry()
    {
        var h = await BuildAsync();
        h.Publishers[PublishTarget.TikTok].Upload = _ => throw new PublishException("400 bad request", retryable: false);
        await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));
        var job = h.Jobs.Jobs.Single();

        await h.Service.RunJobAsync(job.Id); // must NOT throw

        Assert.Equal(PublishJobStatus.Failed, job.Status);
        Assert.True(job.IsPermanentFailure);
        Assert.False(job.CanRetry);
        Assert.Equal(1, h.Publishers[PublishTarget.TikTok].UploadCalls);
    }

    [Fact]
    public async Task A_transient_failure_rethrows_so_the_worker_retries_until_the_cap()
    {
        var h = await BuildAsync();
        h.Publishers[PublishTarget.TikTok].Upload = _ => throw new PublishException("503 upstream", retryable: true);
        await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));
        var job = h.Jobs.Jobs.Single();

        // First three attempts rethrow (retryable + under the cap).
        for (var i = 0; i < PublishJob.MaxAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<PublishException>(() => h.Service.RunJobAsync(job.Id));
            Assert.True(job.CanRetry);
        }

        // The attempt that hits the cap is recorded as permanent and does NOT rethrow.
        await h.Service.RunJobAsync(job.Id);
        Assert.Equal(PublishJob.MaxAttempts, job.AttemptCount);
        Assert.True(job.IsPermanentFailure);
        Assert.False(job.CanRetry);
    }

    [Fact]
    public async Task A_failed_job_can_be_retried_and_then_succeeds()
    {
        var h = await BuildAsync();
        var tiktok = h.Publishers[PublishTarget.TikTok];
        tiktok.Upload = _ => throw new PublishException("503", retryable: true);
        await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));
        var job = h.Jobs.Jobs.Single();
        await Assert.ThrowsAsync<PublishException>(() => h.Service.RunJobAsync(job.Id));

        tiktok.Upload = _ => new PublishResult("ok-123", "https://tiktok.com/@me/video/ok-123");
        var dto = await h.Service.RetryAsync(h.ProjectId, job.Id);
        Assert.Equal(nameof(PublishJobStatus.Pending), dto.Status);
        Assert.Contains(job.Id, h.Scheduler.EnqueuedNow);

        await h.Service.RunJobAsync(job.Id);
        Assert.Equal(PublishJobStatus.Published, job.Status);
        Assert.Equal("ok-123", job.ExternalPostId);
    }

    // ---- configuration gating -------------------------------------

    [Fact]
    public async Task An_unconfigured_platform_is_skipped_with_a_clear_reason_and_no_job()
    {
        var h = await BuildAsync();
        h.Publishers[PublishTarget.TikTok].IsConfigured = false;

        var result = await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok));

        Assert.Empty(result.Created);
        Assert.Contains(result.Skipped, s => s.Platform == nameof(PublishTarget.TikTok) && s.Reason.Contains("chưa được cấu hình"));
        Assert.Empty(h.Jobs.Jobs);
    }

    [Fact]
    public async Task A_platform_that_is_configured_but_not_connected_is_skipped()
    {
        var h = await BuildAsync(connectAll: false);
        h.Connections.Connect(PublishTarget.YouTubeShorts);

        var result = await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.TikTok, PublishTarget.YouTubeShorts));

        Assert.Single(result.Created);
        Assert.Equal(nameof(PublishTarget.YouTubeShorts), result.Created[0].Platform);
        Assert.Contains(result.Skipped, s => s.Platform == nameof(PublishTarget.TikTok) && s.Reason.Contains("chưa được kết nối"));
    }

    // ---- Facebook Page as an independent platform --------------------------

    [Fact]
    public async Task Facebook_Page_is_a_first_class_publish_target_alongside_the_others()
    {
        var h = await BuildAsync();

        var result = await h.Service.PublishAsync(h.ProjectId, Now(AllPlatforms));

        Assert.Equal(4, result.Created.Count);
        Assert.Contains(result.Created, c => c.Platform == nameof(PublishTarget.FacebookPage));
        Assert.Equal(4, h.Scheduler.EnqueuedNow.Count);
    }

    [Fact]
    public async Task A_Facebook_failure_is_isolated_from_TikTok_YouTube_and_Instagram()
    {
        var h = await BuildAsync();
        h.Publishers[PublishTarget.FacebookPage].Upload = _ => throw new PublishException("Page bị hạn chế", retryable: false);

        await h.Service.PublishAsync(h.ProjectId, Now(AllPlatforms));
        foreach (var job in h.Jobs.Jobs)
        {
            await h.Service.RunJobAsync(job.Id);
        }

        var byPlatform = h.Jobs.Jobs.ToDictionary(j => j.Platform);
        Assert.Equal(PublishJobStatus.Failed, byPlatform[PublishTarget.FacebookPage].Status);
        Assert.True(byPlatform[PublishTarget.FacebookPage].IsPermanentFailure);
        Assert.Equal(PublishJobStatus.Published, byPlatform[PublishTarget.TikTok].Status);
        Assert.Equal(PublishJobStatus.Published, byPlatform[PublishTarget.YouTubeShorts].Status);
        Assert.Equal(PublishJobStatus.Published, byPlatform[PublishTarget.InstagramReels].Status);
    }

    [Fact]
    public async Task Facebook_publish_passes_the_Page_id_and_Page_token_to_the_provider()
    {
        var h = await BuildAsync();
        PublishUploadRequest? seen = null;
        h.Publishers[PublishTarget.FacebookPage].Upload = req =>
        {
            seen = req;
            return new PublishResult("vid-1", "https://www.facebook.com/PAGE/videos/vid-1");
        };

        await h.Service.PublishAsync(h.ProjectId, Now(PublishTarget.FacebookPage));
        await h.Service.RunJobAsync(h.Jobs.Jobs.Single().Id);

        Assert.NotNull(seen);
        Assert.Equal("acct-FacebookPage", seen!.ExternalAccountId); // Page ID from the connection
        Assert.Equal("token-FacebookPage", seen.AccessToken);       // Page Access Token
        Assert.Equal("https://api.test/content-projects/" + h.ProjectId + "/assets/" + h.VideoAssetId + "/file", seen.Video.PublicUrl);
    }
}
