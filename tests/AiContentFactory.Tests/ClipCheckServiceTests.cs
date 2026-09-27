using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The AI clip check is ONE billable vision call - these tests pin that it is
/// never made twice for the same clip, never made while the project is busy,
/// is recorded as spend, and that the model's answer is parsed defensively.
/// No real AI: the vision provider is a stub.
/// </summary>
public class ClipCheckServiceTests
{
    private sealed class StubVision : IVisionReviewProvider
    {
        public int Calls { get; private set; }
        public VisionReviewRequest? Last { get; private set; }
        public string Answer { get; set; } =
            """{"verdict":"pass","score":88,"characterMatches":true,"actionMatches":true,"issues":[],"summary":"Clip khớp."}""";
        public bool IsConfigured { get; set; } = true;

        public Task<VisionReviewResult> ReviewAsync(VisionReviewRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = request;
            return Task.FromResult(new VisionReviewResult(Answer, "stub-vision"));
        }
    }

    private sealed class StubFrames : IVideoFrameExtractor
    {
        public List<double?> Requested { get; } = new();

        public Task<byte[]> ExtractFrameAsync(string absoluteVideoPath, double? atSeconds, CancellationToken cancellationToken = default, int? maxHeight = null)
        {
            Requested.Add(atSeconds);
            return Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class StubGenerator : ISceneAssetGenerator
    {
        public ReferenceImage? Character { get; init; }

        public Task<SceneGenerationContext> BuildContextAsync(ContentProject project, CancellationToken cancellationToken = default)
        {
            var approved = Character is null
                ? Array.Empty<ApprovedSceneReference>()
                : new[] { new ApprovedSceneReference(AssetReferenceType.Character, Character) };
            return Task.FromResult(new SceneGenerationContext(project.Id, "9:16", PresetCatalog.ResolveStyle(null), null!, Character, null, approved));
        }

        public Task GenerateClipAsync(SceneGenerationContext context, SceneResponse scene, bool refreshPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task GenerateVoiceAsync(SceneGenerationContext context, SceneResponse scene, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record Harness(
        ClipCheckService Service, ContentProject Project, Scene Scene, StubVision Vision, StubFrames Frames,
        FakeAiUsageTracker Usage, FakeAssetService Assets);

    private static async Task<Harness> BuildAsync(bool withClip = true, bool withCharacter = true)
    {
        var project = ContentProject.Create("Cats", "topic", "storytelling", 60, "9:16", "en");
        var storyboard = Storyboard.Create(project.Id);
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);
        scene.SetGenerationPromptText("A golden cat steps into a sunbeam.", null);
        var assets = new FakeAssetService();
        if (withClip)
        {
            await assets.CreateAsync(project.Id, new CreateAssetRequest(scene.Id, AssetType.Video, "flow", null, "clips/a.mp4", 8, null, null));
        }

        var projects = new FakeContentProjectRepository(project);
        var vision = new StubVision();
        var frames = new StubFrames();
        var usage = new FakeAiUsageTracker();
        var service = new ClipCheckService(
            new FakeStoryboardRepository(storyboard),
            projects,
            new ProjectJobReservationService(projects),
            new StubGenerator { Character = withCharacter ? new ReferenceImage(new byte[] { 9 }, "image/png", "Bastet") : null },
            assets,
            new FakeFileStorage(),
            new FakeMediaProbe(FakeMediaProbe.GoodInfo(duration: 8)),
            frames,
            vision,
            usage,
            Options.Create(new PricingOptions()),
            NullLogger<ClipCheckService>.Instance);
        return new Harness(service, project, scene, vision, frames, usage, assets);
    }

    [Fact]
    public async Task One_check_sends_the_references_and_three_frames_and_records_the_estimated_spend()
    {
        var h = await BuildAsync();

        var result = await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false);

        Assert.Equal(1, h.Vision.Calls);
        Assert.Equal(4, h.Vision.Last!.Images.Count); // 1 reference + 3 frames
        Assert.StartsWith("Reference image: Bastet", h.Vision.Last.Images[0].Label);
        Assert.Equal(new double?[] { 8 * 0.15, 8 * 0.5, 8 * 0.9 }, h.Frames.Requested);
        Assert.Contains("A golden cat steps into a sunbeam.", h.Vision.Last.Question);
        var usage = Assert.Single(h.Usage.Records);
        Assert.Equal("clip_check", usage.Operation);
        Assert.Equal(new PricingOptions().ClipCheckUsd, usage.EstimatedCostUsd);
        Assert.Equal("pass", result.Verdict);
        Assert.Equal(88, result.Score);
        Assert.True(h.Project.Progress.IsIdle); // lock released
    }

    [Fact]
    public async Task Asking_again_for_the_same_clip_is_free_and_makes_no_second_call()
    {
        var h = await BuildAsync();
        await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false);

        var again = await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false);

        Assert.Equal(1, h.Vision.Calls);
        Assert.Single(h.Usage.Records);
        Assert.Equal(0m, again.EstimatedCostUsd);
    }

    [Fact]
    public async Task Force_or_a_new_clip_runs_a_new_paid_check()
    {
        var h = await BuildAsync();
        await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false);

        await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: true);
        await Task.Delay(5);
        await h.Assets.SupersedeSceneAssetsAsync(h.Project.Id, h.Scene.Id, AssetType.Video);
        await h.Assets.CreateAsync(h.Project.Id, new CreateAssetRequest(h.Scene.Id, AssetType.Video, "flow", null, "clips/b.mp4", 8, null, null));
        await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false);

        Assert.Equal(3, h.Vision.Calls);
    }

    [Fact]
    public async Task A_busy_project_is_rejected_before_any_paid_call()
    {
        var h = await BuildAsync();
        h.Project.ReportProgress("render", 0, 1, "busy");

        await Assert.ThrowsAsync<DomainException>(() => h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false));
        Assert.Equal(0, h.Vision.Calls);
    }

    [Fact]
    public async Task No_clip_or_no_api_key_gives_a_clear_error_and_no_call()
    {
        var noClip = await BuildAsync(withClip: false);
        await Assert.ThrowsAsync<DomainException>(() => noClip.Service.CheckAsync(noClip.Project.Id, noClip.Scene.Id, force: false));
        Assert.Equal(0, noClip.Vision.Calls);

        var noKey = await BuildAsync();
        noKey.Vision.IsConfigured = false;
        await Assert.ThrowsAsync<DomainException>(() => noKey.Service.CheckAsync(noKey.Project.Id, noKey.Scene.Id, force: false));
        Assert.Equal(0, noKey.Vision.Calls);
    }

    [Fact]
    public async Task An_unreadable_answer_is_still_recorded_as_spend_and_releases_the_lock()
    {
        var h = await BuildAsync();
        h.Vision.Answer = "not json";

        await Assert.ThrowsAsync<DomainException>(() => h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false));
        Assert.Single(h.Usage.Records);
        Assert.True(h.Project.Progress.IsIdle);
    }

    [Fact]
    public async Task A_shot_without_a_character_never_sends_the_character_image()
    {
        var h = await BuildAsync();
        h.Scene.SetCharacterOnScreen(false);

        var result = await h.Service.CheckAsync(h.Project.Id, h.Scene.Id, force: false);

        Assert.Equal(3, h.Vision.Last!.Images.Count); // frames only
        Assert.Contains("expected on screen: no", h.Vision.Last.Question);
        Assert.Null(result.CharacterMatches);
    }

    [Theory]
    [InlineData("""{"verdict":"FAIL","score":"140","actionMatches":false,"issues":["Mèo có 5 chân"],"summary":"Lỗi."}""", "fail", 100)]
    [InlineData("""{"verdict":"maybe","score":-3}""", "warn", 0)]
    public void The_answer_is_normalised(string json, string verdict, int score)
    {
        var result = ClipCheckService.Parse(json, Guid.NewGuid(), "m", 0.003m, characterExpected: true);

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(score, result.Score);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
    }

    [Fact]
    public void A_stored_result_round_trips()
    {
        var original = ClipCheckService.Parse("""{"verdict":"pass","score":90,"characterMatches":true,"actionMatches":true,"issues":["x"],"summary":"ok"}""",
            Guid.NewGuid(), "m", 0.003m, characterExpected: true);

        var back = ClipCheckResult.FromJson(original.ToJson());

        Assert.Equal(original.AssetId, back!.AssetId);
        Assert.Equal(new[] { "x" }, back.Issues);
        Assert.Null(ClipCheckResult.FromJson("garbage"));
    }
}
