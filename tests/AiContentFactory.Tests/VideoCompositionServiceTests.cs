using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class VideoCompositionServiceTests : IDisposable
{
    private readonly string _output;
    private readonly AudioMixingService _mixing = new(Options.Create(new AudioMixOptions()));

    public VideoCompositionServiceTests()
    {
        _output = Path.Combine(Path.GetTempPath(), $"comp-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(_output, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        try { File.Delete(_output); } catch { /* ignore */ }
    }

    private Timeline BuildTimeline(double total = 61)
    {
        var cues = new List<CaptionCue>
        {
            new(0, 3, new[] { new CaptionWord("a", 0, 3) }),
            new(3, 6, new[] { new CaptionWord("b", 3, 6) }),
        };
        var scenes = new List<TimelineScene>
        {
            new(1, "/v1.mp4", false, SceneMotion.None, 0, total / 2, "/a1.wav", TransitionKind.None, cues.Take(1).ToList()),
            new(2, "/v2.png", true, SceneMotion.KenBurnsIn, total / 2, total / 2, "/a2.wav", TransitionKind.Fade, cues.Skip(1).ToList()),
        };
        var mix = _mixing.BuildSpec(new AudioMixRequest(null, null, null, total));
        return new Timeline(scenes, cues, mix, total, 55, 75);
    }

    private VideoCompositionService Build(IVideoRenderer renderer, MediaInfo probe) =>
        new(renderer, new VideoQualityValidator(new FakeMediaProbe(probe)), NullLogger<VideoCompositionService>.Instance);

    private CompositionRequest Request(Timeline timeline) => new(
        Guid.NewGuid(), timeline, CaptionSettings.Default(), _output,
        BackgroundMusicAbsolutePath: null,
        StoryboardSceneCount: timeline.SceneCount,
        ScenesWithVisual: timeline.SceneCount);

    [Fact]
    public async Task A_valid_render_that_passes_probing_returns_a_result()
    {
        var renderer = new FakeVideoRenderer { ReportedDuration = 61 };
        var svc = Build(renderer, FakeMediaProbe.GoodInfo(61));

        var result = await svc.ComposeAsync(Request(BuildTimeline(61)));

        Assert.True(result.Validation.IsValid, result.Validation.Summary);
        Assert.Equal(61, result.DurationSeconds, 1);
        Assert.Equal(1, renderer.Calls);
    }

    [Fact]
    public async Task The_timeline_audio_mix_and_cues_are_handed_to_the_renderer()
    {
        var renderer = new FakeVideoRenderer();
        var svc = Build(renderer, FakeMediaProbe.GoodInfo(61));
        var timeline = BuildTimeline(61);

        await svc.ComposeAsync(Request(timeline));

        Assert.Same(timeline.Audio, renderer.LastRequest!.AudioMix);
        Assert.Equal(timeline.AllCues.Count, renderer.LastRequest!.Cues.Count);
        Assert.Equal(1080, renderer.LastRequest!.Width);
        Assert.Equal(1920, renderer.LastRequest!.Height);
    }

    [Fact]
    public async Task A_renderer_exception_fails_composition_with_a_render_error()
    {
        var renderer = new FakeVideoRenderer { Throw = new InvalidOperationException("ffmpeg exited with code 1") };
        var svc = Build(renderer, FakeMediaProbe.GoodInfo(61));

        var ex = await Assert.ThrowsAsync<VideoCompositionException>(() => svc.ComposeAsync(Request(BuildTimeline(61))));

        Assert.Contains(ex.Report.Errors, e => e.Contains("render did not complete"));
    }

    [Fact]
    public async Task A_video_with_no_audio_stream_never_passes_composition()
    {
        var renderer = new FakeVideoRenderer();
        var noAudio = new MediaInfo(true, null, 61, 1080, 1920, 30, HasVideo: true, HasAudio: false, AudioDurationSeconds: 0);
        var svc = Build(renderer, noAudio);

        var ex = await Assert.ThrowsAsync<VideoCompositionException>(() => svc.ComposeAsync(Request(BuildTimeline(61))));

        Assert.Contains(ex.Report.Errors, e => e.Contains("narrator audio is missing"));
    }

    [Fact]
    public async Task A_non_9x16_render_is_rejected()
    {
        var renderer = new FakeVideoRenderer();
        var landscape = new MediaInfo(true, null, 61, 1920, 1080, 30, true, true, 61);
        var svc = Build(renderer, landscape);

        var ex = await Assert.ThrowsAsync<VideoCompositionException>(() => svc.ComposeAsync(Request(BuildTimeline(61))));

        Assert.Contains(ex.Report.Errors, e => e.Contains("expected 9:16"));
    }
}
