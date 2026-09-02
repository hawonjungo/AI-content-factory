using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class TimelineServiceTests
{
    private readonly AudioTimingService _timing = new();
    private readonly TimelineOptions _options = new();

    private TimelineService Build() => new(
        new CaptionSegmentationService(),
        new AudioMixingService(Options.Create(new AudioMixOptions())),
        Options.Create(_options),
        Options.Create(new CaptionSegmentationOptions()));

    private TimelineSceneInput Scene(int n, double narrationSeconds, bool still, string? text = null)
    {
        text ??= $"This is the narration for scene number {n} and it runs on a little.";
        var timing = narrationSeconds > 0 ? _timing.Compute(text, narrationSeconds) : AudioTiming.Empty;
        return new TimelineSceneInput(n, $"/vis/{n}.{(still ? "png" : "mp4")}", still,
            narrationSeconds > 0 ? $"/voice/{n}.wav" : null, timing, text);
    }

    private TimelineBuildRequest Request(params TimelineSceneInput[] scenes) =>
        new(scenes, CaptionSettings.Default());

    [Fact]
    public void Scene_duration_equals_its_narration_length()
    {
        var timeline = Build().Build(Request(Scene(1, 9.0, still: false), Scene(2, 7.5, still: true)));

        Assert.Equal(9.0, timeline.Scenes[0].DurationSeconds, 1);
        Assert.Equal(7.5, timeline.Scenes[1].DurationSeconds, 1);
    }

    [Fact]
    public void Narration_is_the_authority_not_the_storyboard_estimate()
    {
        // Storyboard would have planned ~8s; the actual narration is 14s.
        var timeline = Build().Build(Request(Scene(1, 14.0, still: false)));

        Assert.Equal(14.0, timeline.Scenes[0].DurationSeconds, 1);
    }

    [Fact]
    public void A_silent_scene_gets_a_short_fixed_length_never_filler()
    {
        var timeline = Build().Build(Request(Scene(1, 6.0, false), Scene(2, 0, still: true)));

        Assert.Equal(_options.NoNarrationSceneSeconds, timeline.Scenes[1].DurationSeconds, 2);
    }

    [Fact]
    public void An_unusually_long_narration_is_clamped()
    {
        var timeline = Build().Build(Request(Scene(1, 40.0, false)));

        Assert.Equal(_options.MaxSceneSeconds, timeline.Scenes[0].DurationSeconds, 2);
    }

    [Fact]
    public void Total_is_the_sum_of_scene_durations_and_offsets_are_cumulative()
    {
        var timeline = Build().Build(Request(Scene(1, 8.0, false), Scene(2, 9.0, true), Scene(3, 7.0, false)));

        Assert.Equal(timeline.Scenes.Sum(s => s.DurationSeconds), timeline.TotalSeconds, 3);
        Assert.Equal(0, timeline.Scenes[0].StartSeconds, 3);
        Assert.Equal(timeline.Scenes[0].DurationSeconds, timeline.Scenes[1].StartSeconds, 3);
        Assert.Equal(timeline.Scenes[0].DurationSeconds + timeline.Scenes[1].DurationSeconds, timeline.Scenes[2].StartSeconds, 3);
    }

    [Fact]
    public void Image_scenes_get_a_camera_move_video_scenes_do_not()
    {
        var timeline = Build().Build(Request(Scene(1, 8.0, still: false), Scene(2, 8.0, still: true), Scene(3, 8.0, still: true)));

        Assert.Equal(SceneMotion.None, timeline.Scenes[0].Motion);
        Assert.NotEqual(SceneMotion.None, timeline.Scenes[1].Motion);
        Assert.NotEqual(SceneMotion.None, timeline.Scenes[2].Motion);
        Assert.NotEqual(timeline.Scenes[1].Motion, timeline.Scenes[2].Motion); // cycles, no slideshow
    }

    [Fact]
    public void First_scene_has_no_transition_the_rest_fade_in()
    {
        var timeline = Build().Build(Request(Scene(1, 8.0, false), Scene(2, 8.0, false), Scene(3, 8.0, true)));

        Assert.Equal(TransitionKind.None, timeline.Scenes[0].TransitionIn);
        Assert.Equal(TransitionKind.Fade, timeline.Scenes[1].TransitionIn);
        Assert.Equal(TransitionKind.Fade, timeline.Scenes[2].TransitionIn);
    }

    [Fact]
    public void Captions_are_segmented_and_bucketed_onto_their_scene()
    {
        var timeline = Build().Build(Request(Scene(1, 9.0, false), Scene(2, 9.0, true)));

        Assert.NotEmpty(timeline.AllCues);
        foreach (var scene in timeline.Scenes)
        {
            Assert.All(scene.Cues, c => Assert.InRange(c.StartSeconds, scene.StartSeconds - 0.001, scene.EndSeconds));
        }
        Assert.Equal(timeline.AllCues.Count, timeline.Scenes.Sum(s => s.Cues.Count));
    }

    [Fact]
    public void A_roughly_one_minute_set_of_scenes_lands_in_the_target_range()
    {
        var scenes = new[]
        {
            Scene(1, 8.0, false), Scene(2, 9.0, true), Scene(3, 8.0, true),
            Scene(4, 9.0, false), Scene(5, 8.0, true), Scene(6, 9.0, false), Scene(7, 9.0, true),
        };

        var timeline = Build().Build(Request(scenes));

        Assert.InRange(timeline.TotalSeconds, 55, 75);
        Assert.True(timeline.WithinTargetRange);
    }

    [Fact]
    public void A_too_short_timeline_is_flagged_out_of_range()
    {
        var timeline = Build().Build(Request(Scene(1, 8.0, false), Scene(2, 9.0, true)));

        Assert.False(timeline.WithinTargetRange);
    }

    [Fact]
    public void The_audio_mix_spec_picks_up_the_music_track()
    {
        var request = new TimelineBuildRequest(
            new[] { Scene(1, 8.0, false) },
            CaptionSettings.Default(),
            MusicPath: "/music/bed.mp3");

        var timeline = Build().Build(request);

        Assert.Equal("/music/bed.mp3", timeline.Audio.MusicPath);
        Assert.True(timeline.Audio.HasBed);
    }
}
