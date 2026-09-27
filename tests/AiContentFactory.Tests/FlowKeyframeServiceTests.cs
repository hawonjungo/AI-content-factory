using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The free half of the two-step Flow workflow: a first frame uploaded from
/// Flow, or taken from the previous clip's last frame. No AI provider is
/// involved; storage, probe and FFmpeg are fakes.
/// </summary>
public class FlowKeyframeServiceTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    private sealed class FakeFrameExtractor : IVideoFrameExtractor
    {
        public string? LastPath { get; private set; }
        public double? LastAt { get; private set; } = -1;

        public Task<byte[]> ExtractFrameAsync(string absoluteVideoPath, double? atSeconds, CancellationToken cancellationToken = default, int? maxHeight = null)
        {
            LastPath = absoluteVideoPath;
            LastAt = atSeconds;
            return Task.FromResult(Png);
        }
    }

    private readonly Guid _project = Guid.NewGuid();

    private (FlowKeyframeService Service, Storyboard Storyboard, FakeAssetService Assets, FakeFileStorage Files, FakeFrameExtractor Frames)
        Build(MediaInfo? probe = null)
    {
        var storyboard = Storyboard.Create(_project);
        var assets = new FakeAssetService();
        var files = new FakeFileStorage();
        var frames = new FakeFrameExtractor();
        var service = new FlowKeyframeService(
            new FakeStoryboardRepository(storyboard), assets, files,
            new FakeMediaProbe(probe ?? FakeMediaProbe.GoodInfo(duration: 0, w: 1080, h: 1920)),
            frames, NullLogger<FlowKeyframeService>.Instance);
        return (service, storyboard, assets, files, frames);
    }

    [Fact]
    public async Task An_uploaded_png_becomes_the_approved_first_frame()
    {
        var (service, storyboard, assets, files, _) = Build();
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);

        var result = await service.UploadFirstFrameAsync(_project, scene.Id, "frame.png", new MemoryStream(Png));

        Assert.Equal("Approved", result.KeyframeStatus);
        var image = Assert.Single(assets.All, a => a.Type == nameof(AssetType.Image));
        Assert.Equal(image.Id, scene.KeyframeAssetId);
        Assert.EndsWith(".png", image.FilePath);
        Assert.Single(files.Files);
    }

    [Fact]
    public async Task The_stored_extension_follows_the_real_bytes_not_the_file_name()
    {
        var (service, storyboard, assets, _, _) = Build();
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);

        await service.UploadFirstFrameAsync(_project, scene.Id, "frame.png", new MemoryStream(Jpeg));

        Assert.EndsWith(".jpg", assets.All.Single().FilePath);
    }

    [Theory]
    [InlineData("frame.webp")]
    [InlineData("frame.exe")]
    [InlineData(null)]
    public async Task Other_file_types_are_rejected(string? fileName)
    {
        var (service, storyboard, _, files, _) = Build();
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);

        await Assert.ThrowsAsync<DomainException>(() => service.UploadFirstFrameAsync(_project, scene.Id, fileName, new MemoryStream(Png)));
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_rejected_even_with_an_image_name()
    {
        var (service, storyboard, _, files, _) = Build();
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);

        await Assert.ThrowsAsync<DomainException>(() =>
            service.UploadFirstFrameAsync(_project, scene.Id, "frame.png", new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })));
        Assert.Empty(files.Files);
    }

    [Fact]
    public async Task An_unreadable_image_is_rejected_and_its_file_removed()
    {
        var (service, storyboard, assets, files, _) = Build(MediaInfo.Unreadable("bad"));
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);

        await Assert.ThrowsAsync<DomainException>(() => service.UploadFirstFrameAsync(_project, scene.Id, "frame.png", new MemoryStream(Png)));
        Assert.Empty(files.Files);
        Assert.Empty(assets.All);
        Assert.Equal(KeyframeStatus.None, scene.KeyframeStatus);
    }

    [Fact]
    public async Task A_scene_that_already_has_an_imported_clip_is_never_silently_replaced()
    {
        var (service, storyboard, _, _, _) = Build();
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);
        scene.SetSkipGeneration(true);

        await Assert.ThrowsAsync<DomainException>(() => service.UploadFirstFrameAsync(_project, scene.Id, "frame.png", new MemoryStream(Png)));
    }

    [Fact]
    public async Task The_previous_clips_last_frame_becomes_this_scenes_first_frame()
    {
        var (service, storyboard, assets, _, frames) = Build();
        var first = storyboard.AddScene(8, "a", "", "", SceneVisualType.AiVideo);
        var second = storyboard.AddScene(8, "b", "", "", SceneVisualType.AiVideo);
        await assets.CreateAsync(_project, new CreateAssetRequest(first.Id, AssetType.Video, "flow", null, "clips/old.mp4", 8, null, null));
        await Task.Delay(5);
        await assets.CreateAsync(_project, new CreateAssetRequest(first.Id, AssetType.Video, "flow", null, "clips/new.mp4", 8, null, null));

        var result = await service.UsePreviousClipLastFrameAsync(_project, second.Id);

        Assert.Equal("clips/new.mp4", frames.LastPath); // newest ready clip of the previous scene
        Assert.Null(frames.LastAt); // the very last frame
        Assert.Equal("Approved", result.KeyframeStatus);
        Assert.NotNull(second.KeyframeAssetId);
    }

    [Fact]
    public async Task The_first_scene_or_a_previous_scene_without_a_clip_gives_a_clear_error()
    {
        var (service, storyboard, _, _, _) = Build();
        var first = storyboard.AddScene(8, "a", "", "", SceneVisualType.AiVideo);
        var second = storyboard.AddScene(8, "b", "", "", SceneVisualType.AiVideo);

        var noPrevious = await Assert.ThrowsAsync<DomainException>(() => service.UsePreviousClipLastFrameAsync(_project, first.Id));
        Assert.Contains("cảnh đầu tiên", noPrevious.Message);

        var noClip = await Assert.ThrowsAsync<DomainException>(() => service.UsePreviousClipLastFrameAsync(_project, second.Id));
        Assert.Contains("chưa có clip", noClip.Message);
    }

    [Fact]
    public void The_motion_prompt_describes_only_movement_and_locks_the_first_frame()
    {
        var prompt = VideoPromptBuilder.BuildMotion(new VideoPromptSpec(
            "A golden cat steps into a sunbeam", CameraMovement.SlowPushIn, true, false,
            "photorealistic, handheld camera feel", 8, "9:16", CharacterLabels: new[] { "Bastet" }, Shot: ShotSize.CloseUp));

        Assert.StartsWith("Close-up, slow subtle push-in. Starting exactly from the provided first frame: a golden cat steps into a sunbeam.", prompt);
        Assert.Contains("exactly as in the first frame", prompt);
        Assert.Contains(VideoPromptBuilder.AmbientAudioSentence, prompt);
        Assert.DoesNotContain("photorealistic", prompt); // the frame owns the style
        Assert.DoesNotContain("Bastet", prompt); // the frame owns who is on screen
    }
}
