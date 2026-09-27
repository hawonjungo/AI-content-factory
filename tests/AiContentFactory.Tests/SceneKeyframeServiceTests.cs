using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="SceneKeyframeService"/>: Step 5's two-stage Keyframe -&gt;
/// Video workflow. Stage 1 (<see cref="SceneKeyframeService.GenerateKeyframeAsync"/>)
/// generates a still image and stores it as an ordinary Image <c>Asset</c>,
/// tracked via <c>Scene.KeyframeStatus</c>. Stage 2
/// (<see cref="SceneKeyframeService.GenerateVideoFromKeyframeAsync"/>) only
/// runs once that Keyframe is approved, and animates it via the standard Veo
/// image-to-video call (<c>VideoGenerationRequest.InitialImage</c>).
/// </summary>
public class SceneKeyframeServiceTests
{
    /// <summary>Only implements BuildContextAsync - the one member SceneKeyframeService actually calls.</summary>
    private sealed class FakeSceneAssetGenerator : ISceneAssetGenerator
    {
        public SceneGenerationContext Context { get; set; } = null!;

        public Task<SceneGenerationContext> BuildContextAsync(ContentProject project, CancellationToken cancellationToken = default) =>
            Task.FromResult(Context);

        public Task GenerateClipAsync(SceneGenerationContext context, SceneResponse scene, bool refreshPrompt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task GenerateVoiceAsync(SceneGenerationContext context, SceneResponse scene, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static ContentProject Project() => ContentProject.Create("A Project", "topic", "niche", 60, "9:16", "en");

    private static (SceneKeyframeService Service, FakeImageGenerationProvider Image, FakeVideoGenerationProvider Video, FakeAssetService Assets, FakeStoryboardRepository StoryboardRepo, FakeAiUsageTracker Usage, FakeContentProjectRepository ProjectRepo)
        Build(ContentProject project)
    {
        var storyboardRepo = new FakeStoryboardRepository(Storyboard.Create(project.Id));
        var projectRepo = new FakeContentProjectRepository(project);
        var jobReservation = new ProjectJobReservationService(projectRepo);
        var sceneAssetGenerator = new FakeSceneAssetGenerator
        {
            Context = new SceneGenerationContext(project.Id, "9:16", PresetCatalog.ResolveStyle(null), null!, null, null, Array.Empty<ApprovedSceneReference>())
        };
        var storyVisualContext = new FakeStoryVisualContextResolver();
        var image = new FakeImageGenerationProvider();
        var video = new FakeVideoGenerationProvider();
        var assets = new FakeAssetService();
        var fileStorage = new FakeFileStorage();
        var usage = new FakeAiUsageTracker();
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);

        var service = new SceneKeyframeService(
            storyboardRepo, projectRepo, jobReservation, sceneAssetGenerator, storyVisualContext,
            image, video, assets, fileStorage, usage, ledger,
            Options.Create(new PricingOptions()), Options.Create(new CreditCostOptions()),
            NullLogger<SceneKeyframeService>.Instance);

        return (service, image, video, assets, storyboardRepo, usage, projectRepo);
    }

    private static Scene SeedScene(FakeStoryboardRepository repo, string visualDescription = "A cat sits by a window.")
    {
        var scene = repo.Current!.AddScene(8, "narration text", visualDescription, "push", SceneVisualType.AiVideo);
        return scene;
    }

    [Fact]
    public async Task Generate_keyframe_marks_the_scene_Generated_and_stores_an_image_asset()
    {
        var project = Project();
        var (service, image, _, assets, repo, usage, _) = Build(project);
        var scene = SeedScene(repo);

        var result = await service.GenerateKeyframeAsync(project.Id, scene.Id);

        Assert.Equal("Generated", result.KeyframeStatus);
        Assert.NotNull(result.KeyframeAssetId);
        Assert.Equal(1, image.Calls);
        Assert.Single(assets.All);
        Assert.Equal("Image", assets.All[0].Type);
        Assert.Single(usage.Records);
        Assert.Equal("scene_keyframe_generation", usage.Records[0].Operation);
    }

    [Fact]
    public async Task Keyframe_prompt_never_contains_raw_narration()
    {
        var project = Project();
        var (service, image, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo, visualDescription: "A cozy kitchen at sunset.");

        await service.GenerateKeyframeAsync(project.Id, scene.Id);

        Assert.Contains("A cozy kitchen at sunset.", image.LastRequest!.Prompt);
        Assert.DoesNotContain("narration text", image.LastRequest!.Prompt);
    }

    [Fact]
    public async Task Generate_keyframe_with_no_visual_description_or_prompt_throws()
    {
        var project = Project();
        var (service, _, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo, visualDescription: "");

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateKeyframeAsync(project.Id, scene.Id));
        Assert.Equal(KeyframeStatus.None, scene.KeyframeStatus);
    }

    [Fact]
    public async Task A_provider_failure_marks_the_keyframe_Failed_and_rethrows()
    {
        var project = Project();
        var (service, image, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);
        image.Throw = new HttpRequestException("image API 500");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GenerateKeyframeAsync(project.Id, scene.Id));

        Assert.Equal(KeyframeStatus.Failed, scene.KeyframeStatus);
    }

    [Fact]
    public async Task Regenerating_an_approved_keyframe_demotes_it_back_to_Generated()
    {
        var project = Project();
        var (service, _, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);
        await service.GenerateKeyframeAsync(project.Id, scene.Id);
        scene.ApproveKeyframe();
        Assert.Equal(KeyframeStatus.Approved, scene.KeyframeStatus);

        var result = await service.GenerateKeyframeAsync(project.Id, scene.Id);

        Assert.Equal("Generated", result.KeyframeStatus);
    }

    [Fact]
    public async Task Generate_video_without_an_approved_keyframe_throws()
    {
        var project = Project();
        var (service, _, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateVideoFromKeyframeAsync(project.Id, scene.Id));
    }

    [Fact]
    public async Task Generate_video_from_an_approved_keyframe_uses_it_as_the_initial_image_and_marks_the_scene_Generated()
    {
        var project = Project();
        var (service, _, video, assets, repo, usage, _) = Build(project);
        var scene = SeedScene(repo);
        await service.GenerateKeyframeAsync(project.Id, scene.Id);
        scene.ApproveKeyframe();

        var result = await service.GenerateVideoFromKeyframeAsync(project.Id, scene.Id);

        Assert.Equal("Generated", result.Status);
        Assert.NotNull(video.LastRequest!.InitialImage);
        Assert.Equal("Keyframe", video.LastRequest!.InitialImage!.Label);
        Assert.Contains(assets.All, a => a.Type == "Video");
        Assert.Contains(usage.Records, r => r.Operation == "keyframe_video_generation");
    }

    [Fact]
    public async Task Generate_video_does_not_blindly_copy_the_image_prompt_as_the_motion_prompt()
    {
        var project = Project();
        var (service, _, video, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo, visualDescription: "A cat sits by a window.");
        await service.GenerateKeyframeAsync(project.Id, scene.Id);
        scene.ApproveKeyframe();
        scene.SetMotionPrompt("The cat slowly turns its head toward the door.");

        await service.GenerateVideoFromKeyframeAsync(project.Id, scene.Id);

        Assert.Equal("The cat slowly turns its head toward the door.", video.LastRequest!.Prompt);
    }

    [Fact]
    public async Task Generate_keyframe_while_one_is_already_generating_throws()
    {
        var project = Project();
        var (service, _, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);
        scene.MarkKeyframeGenerating();

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateKeyframeAsync(project.Id, scene.Id));
    }

    [Fact]
    public async Task Generate_video_while_already_generating_throws()
    {
        var project = Project();
        var (service, _, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);
        await service.GenerateKeyframeAsync(project.Id, scene.Id);
        scene.ApproveKeyframe();
        scene.MarkGenerating();

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateVideoFromKeyframeAsync(project.Id, scene.Id));
    }

    [Fact]
    public async Task Generate_keyframe_while_the_project_is_busy_with_another_operation_throws_without_calling_the_provider()
    {
        var project = Project();
        var (service, image, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);
        project.ReportProgress("render", 0, 1, "Đang ghép video");

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateKeyframeAsync(project.Id, scene.Id));

        Assert.Equal(0, image.Calls);
        Assert.Equal(KeyframeStatus.None, scene.KeyframeStatus);
    }

    [Fact]
    public async Task Generate_keyframe_clears_the_projects_busy_flag_after_success_so_a_later_operation_can_proceed()
    {
        var project = Project();
        var (service, _, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);

        await service.GenerateKeyframeAsync(project.Id, scene.Id);

        Assert.True(project.Progress.IsIdle);
    }

    [Fact]
    public async Task Generate_keyframe_clears_the_projects_busy_flag_after_a_failure_too()
    {
        var project = Project();
        var (service, image, _, _, repo, _, _) = Build(project);
        var scene = SeedScene(repo);
        image.Throw = new HttpRequestException("image API 500");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GenerateKeyframeAsync(project.Id, scene.Id));

        Assert.True(project.Progress.IsIdle);
    }
}
