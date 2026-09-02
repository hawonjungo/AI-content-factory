using System.Text;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class FlowClipImportServiceTests
{
    private readonly Guid _project = Guid.NewGuid();
    private readonly Storyboard _storyboard;
    private readonly Guid _videoScene1;
    private readonly Guid _videoScene2;
    private readonly Guid _imageScene;

    public FlowClipImportServiceTests()
    {
        _storyboard = Storyboard.Create(_project);
        var s1 = _storyboard.AddScene(8, "Scene one narration.", "shot", "push", SceneVisualType.AiVideo);
        s1.SetAllocation(100, "Fast", "hero");
        var s2 = _storyboard.AddScene(8, "Scene two narration.", "shot", "pan", SceneVisualType.AiVideo);
        s2.SetAllocation(60, "Lite", "beat");
        var s3 = _storyboard.AddScene(8, "Scene three.", "card", "none", SceneVisualType.AiImage);
        s3.SetAllocation(20, null, "still");
        _videoScene1 = s1.Id;
        _videoScene2 = s2.Id;
        _imageScene = s3.Id;
    }

    private (FlowClipImportService Service, FakeAssetService Assets, CreditLedgerService Ledger, InMemoryGenerationAttemptRepository Attempts)
        Build(MediaInfo probe)
    {
        var repo = new FakeStoryboardRepository(_storyboard);
        var assets = new FakeAssetService();
        var attempts = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attempts, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        var service = new FlowClipImportService(
            new FakeStoryboardService(repo),
            repo,
            assets,
            new FakeFileStorage(),
            new FakeMediaProbe(probe),
            ledger,
            Options.Create(new CreditCostOptions()),
            NullLogger<FlowClipImportService>.Instance);
        return (service, assets, ledger, attempts);
    }

    private static Stream Clip() => new MemoryStream(Encoding.ASCII.GetBytes("fake mp4 bytes"));

    [Fact]
    public async Task A_valid_vertical_clip_is_accepted_matched_and_the_scene_completed()
    {
        var (service, assets, ledger, _) = Build(FakeMediaProbe.GoodInfo(4.2, 1080, 1920));

        var result = await service.ImportAsync(_project, _videoScene1, "flow-clip.mp4", Clip());

        Assert.True(result.Accepted);
        Assert.Equal("9:16", result.AspectLabel);
        Assert.InRange(result.DurationSeconds, 4.0, 4.5);

        var asset = Assert.Single(assets.All, a => a.SceneId == _videoScene1 && a.Type == "Video" && a.Status == "Ready");
        Assert.Equal(1080, asset.Width);
        Assert.Equal(1920, asset.Height);
        Assert.Equal("google-flow", asset.Provider);

        Assert.Equal(SceneStatus.Generated, _storyboard.Scenes.First(s => s.Id == _videoScene1).Status);

        var usage = await ledger.GetDailyUsageAsync();
        Assert.Equal(20, usage.Used); // one Fast clip's worth of Flow credits booked
    }

    [Fact]
    public async Task Importing_a_clip_flags_the_scene_to_skip_generation()
    {
        var (service, _, _, _) = Build(FakeMediaProbe.GoodInfo(4, 1080, 1920));

        await service.ImportAsync(_project, _videoScene1, "flow.mp4", Clip());

        Assert.True(_storyboard.Scenes.First(s => s.Id == _videoScene1).SkipGeneration);
    }

    [Fact]
    public async Task Turning_skip_off_drops_the_imported_clip_and_returns_the_scene_to_pending()
    {
        var (service, assets, _, _) = Build(FakeMediaProbe.GoodInfo(4, 1080, 1920));
        await service.ImportAsync(_project, _videoScene1, "flow.mp4", Clip());

        await service.SetSkipGenerationAsync(_project, _videoScene1, skip: false);

        var scene = _storyboard.Scenes.First(s => s.Id == _videoScene1);
        Assert.False(scene.SkipGeneration);
        Assert.Equal(SceneStatus.Pending, scene.Status);
        Assert.DoesNotContain(assets.All, a => a.SceneId == _videoScene1 && a.Type == "Video" && a.Status == "Ready");
    }

    [Fact]
    public async Task Turning_skip_on_by_hand_marks_the_scene_without_touching_assets()
    {
        var (service, assets, _, _) = Build(FakeMediaProbe.GoodInfo(4, 1080, 1920));

        await service.SetSkipGenerationAsync(_project, _videoScene2, skip: true);

        Assert.True(_storyboard.Scenes.First(s => s.Id == _videoScene2).SkipGeneration);
        Assert.Empty(assets.All);
    }

    [Fact]
    public async Task An_unreadable_file_is_rejected_and_nothing_is_stored()
    {
        var (service, assets, _, _) = Build(MediaInfo.Unreadable("moov atom not found"));

        var result = await service.ImportAsync(_project, _videoScene1, "broken.mp4", Clip());

        Assert.False(result.Accepted);
        Assert.NotEmpty(result.Issues);
        Assert.Empty(assets.All);
        Assert.Equal(SceneStatus.Pending, _storyboard.Scenes.First(s => s.Id == _videoScene1).Status);
    }

    [Fact]
    public async Task A_zero_duration_clip_is_rejected()
    {
        var (service, _, _, _) = Build(FakeMediaProbe.GoodInfo(0, 1080, 1920));

        var result = await service.ImportAsync(_project, _videoScene1, "empty.mp4", Clip());

        Assert.False(result.Accepted);
        Assert.Contains(result.Issues, i => i.Contains("0 giây"));
    }

    [Fact]
    public async Task A_landscape_clip_is_accepted_but_warned_about()
    {
        var (service, _, _, _) = Build(FakeMediaProbe.GoodInfo(5, 1920, 1080));

        var result = await service.ImportAsync(_project, _videoScene1, "wide.mp4", Clip());

        Assert.True(result.Accepted);
        Assert.Contains(result.Warnings, w => w.Contains("9:16"));
    }

    [Fact]
    public async Task Re_importing_the_same_scene_does_not_double_charge_credits()
    {
        var (service, _, ledger, _) = Build(FakeMediaProbe.GoodInfo(4, 1080, 1920));

        await service.ImportAsync(_project, _videoScene1, "a.mp4", Clip());
        await service.ImportAsync(_project, _videoScene1, "b.mp4", Clip());

        var usage = await ledger.GetDailyUsageAsync();
        Assert.Equal(20, usage.Used); // still just one clip's credits
    }

    [Fact]
    public async Task Status_reports_missing_flow_clips_before_import()
    {
        var (service, _, _, _) = Build(FakeMediaProbe.GoodInfo(4, 1080, 1920));

        var status = await service.GetStatusAsync(_project);

        Assert.Equal(3, status.TotalScenes);
        Assert.Equal(2, status.ScenesNeedingFlow);
        Assert.Equal(0, status.ImportedValid);
        Assert.Equal(2, status.MissingOrInvalid);
        Assert.False(status.ReadyForRender);
        Assert.Contains(status.Scenes, s => s.NeedsFlowClip && s.Issues.Any(i => i.Contains("Google Flow")));
    }

    [Fact]
    public async Task Status_is_ready_once_every_scene_has_a_visual()
    {
        var (service, assets, _, _) = Build(FakeMediaProbe.GoodInfo(4, 1080, 1920));

        await service.ImportAsync(_project, _videoScene1, "1.mp4", Clip());
        await service.ImportAsync(_project, _videoScene2, "2.mp4", Clip());
        // the AI_IMAGE scene's still comes from generate-assets, not Flow
        await assets.CreateAsync(_project, new CreateAssetRequest(_imageScene, AssetType.Image, "gemini-image", null, "path/still.png", null, 1080, 1920));

        var status = await service.GetStatusAsync(_project);

        Assert.Equal(2, status.ImportedValid);
        Assert.Equal(0, status.MissingOrInvalid);
        Assert.True(status.ReadyForRender);
    }
}
