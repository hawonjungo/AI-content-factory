using AiContentFactory.Application.Rendering;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class VideoQualityValidatorTests : IDisposable
{
    private readonly string _file;

    public VideoQualityValidatorTests()
    {
        _file = Path.Combine(Path.GetTempPath(), $"vqv-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(_file, new byte[] { 0, 0, 0, 0 });
    }

    public void Dispose()
    {
        try { File.Delete(_file); } catch { /* ignore */ }
    }

    private static IReadOnlyList<CaptionCue> Cues(params (double s, double e)[] spans) =>
        spans.Select(x => new CaptionCue(x.s, x.e, new[] { new CaptionWord("w", x.s, x.e) })).ToList();

    private VideoValidationContext Context(
        string? path = null,
        double expectedMin = 55,
        double expectedMax = 75,
        double narrationSeconds = 60,
        bool captionsEnabled = true,
        IReadOnlyList<CaptionCue>? cues = null,
        int sceneCount = 6,
        int scenesWithVisual = 6,
        bool renderSucceeded = true,
        bool requireNarration = true) =>
        new(
            path ?? _file,
            TargetWidth: 1080, TargetHeight: 1920, TargetFps: 30,
            ExpectedMinSeconds: expectedMin, ExpectedMaxSeconds: expectedMax,
            NarrationSeconds: narrationSeconds,
            CaptionsEnabled: captionsEnabled,
            Cues: cues ?? Cues((0, 3), (3, 6), (6, 9)),
            SceneCount: sceneCount, ScenesWithVisual: scenesWithVisual,
            RenderSucceeded: renderSucceeded,
            RequireNarration: requireNarration);

    private static VideoQualityValidator Validator(MediaInfo info) => new(new FakeMediaProbe(info));

    [Fact]
    public async Task A_well_formed_9x16_video_passes()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo()).ValidateAsync(Context());

        Assert.True(report.IsValid, report.Summary);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task Render_failure_is_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo()).ValidateAsync(Context(renderSucceeded: false));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("render did not complete"));
    }

    [Fact]
    public async Task A_missing_output_file_is_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo())
            .ValidateAsync(Context(path: Path.Combine(Path.GetTempPath(), "does-not-exist.mp4")));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("output file not found"));
    }

    [Fact]
    public async Task An_unreadable_container_is_reported()
    {
        var report = await Validator(MediaInfo.Unreadable("moov atom not found")).ValidateAsync(Context());

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("not a readable MP4"));
    }

    [Fact]
    public async Task A_missing_audio_stream_is_reported_as_missing_narrator_audio()
    {
        var info = new MediaInfo(true, null, 61, 1080, 1920, 30, HasVideo: true, HasAudio: false, AudioDurationSeconds: 0);
        var report = await Validator(info).ValidateAsync(Context());

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("narrator audio is missing"));
    }

    [Fact]
    public async Task Zero_narration_seconds_is_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo()).ValidateAsync(Context(narrationSeconds: 0));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("no narration audio was produced"));
    }

    [Fact]
    public async Task Zero_narration_seconds_is_fine_when_narration_is_not_required()
    {
        // "Keep original audio" / "Mute": there is deliberately no TTS track,
        // but the muxed clip/silent audio still gives a valid audio stream.
        var report = await Validator(FakeMediaProbe.GoodInfo())
            .ValidateAsync(Context(narrationSeconds: 0, requireNarration: false));

        Assert.True(report.IsValid, report.Summary);
        Assert.DoesNotContain(report.Errors, e => e.Contains("no narration audio was produced"));
    }

    [Fact]
    public async Task A_landscape_output_fails_the_9x16_check()
    {
        var info = new MediaInfo(true, null, 61, 1920, 1080, 30, true, true, 61);
        var report = await Validator(info).ValidateAsync(Context());

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("expected 9:16"));
    }

    [Fact]
    public async Task A_9x16_output_at_the_wrong_resolution_is_reported()
    {
        var info = new MediaInfo(true, null, 61, 720, 1280, 30, true, true, 61); // ratio ok, size wrong
        var report = await Validator(info).ValidateAsync(Context());

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("expected 1080x1920"));
        Assert.DoesNotContain(report.Errors, e => e.Contains("expected 9:16"));
    }

    [Fact]
    public async Task A_duration_outside_the_expected_range_is_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo(duration: 42, audio: 42)).ValidateAsync(Context());

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("outside the expected"));
    }

    [Fact]
    public async Task Audio_video_desync_is_reported()
    {
        var info = new MediaInfo(true, null, 61, 1080, 1920, 30, true, true, AudioDurationSeconds: 45);
        var report = await Validator(info).ValidateAsync(Context());

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("out of sync"));
    }

    [Fact]
    public async Task Captions_enabled_with_no_cues_is_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo())
            .ValidateAsync(Context(cues: Array.Empty<CaptionCue>()));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("no caption cues"));
    }

    [Fact]
    public async Task Overlapping_caption_cues_are_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo())
            .ValidateAsync(Context(cues: Cues((0, 4), (3, 7))));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("overlap"));
    }

    [Fact]
    public async Task A_missing_scene_is_reported()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo())
            .ValidateAsync(Context(sceneCount: 6, scenesWithVisual: 5));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("no visual asset"));
    }

    [Fact]
    public async Task A_wrong_frame_rate_is_a_warning_not_an_error()
    {
        var report = await Validator(FakeMediaProbe.GoodInfo(fps: 24)).ValidateAsync(Context());

        Assert.True(report.IsValid, report.Summary);
        Assert.Contains(report.Warnings, w => w.Contains("frame rate"));
    }
}
