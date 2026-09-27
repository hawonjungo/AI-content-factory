using AiContentFactory.Application.Rendering;
using AiContentFactory.Infrastructure.Rendering;
using Xunit;

namespace AiContentFactory.Tests;

public class FfmpegArgsTests
{
    private static RenderScene VideoScene(string? voice = "/voice/1.wav") =>
        new("/vis/1.mp4", voice, 8.0, Narration: "", IsStillImage: false);

    private static RenderScene StillScene(SceneMotion motion, TransitionKind t = TransitionKind.None) =>
        new("/vis/1.png", "/voice/1.wav", 8.0, Narration: "", IsStillImage: true, Motion: motion, TransitionIn: t);

    [Fact]
    public void A_video_segment_maps_the_voice_track_explicitly_not_the_clip_audio()
    {
        var args = FfmpegArgs.SegmentArgs(VideoScene(), "/out/seg.mp4", 1080, 1920, 30);

        // The bug fix: without these maps ffmpeg auto-picks the Veo clip's own audio.
        Assert.Contains("-map 0:v:0 -map 1:a:0", args);
        Assert.Contains("-i \"/voice/1.wav\"", args);
        Assert.Contains("-stream_loop -1", args);
        // -t is the length authority; the voice is levelled and padded to fill
        // the scene. -shortest is deliberately gone (it truncated short voices).
        Assert.Contains("-t 8.00", args);
        Assert.Contains("dynaudnorm", args);
        Assert.Contains("apad", args);
        Assert.DoesNotContain("-shortest", args);
    }

    [Fact]
    public void A_scene_with_no_narration_still_gets_a_mapped_silent_track()
    {
        var args = FfmpegArgs.SegmentArgs(VideoScene(voice: null), "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("anullsrc", args);
        Assert.Contains("-map 0:v:0 -map 1:a:0", args);
    }

    [Fact]
    public void Audio_mode_Clip_keeps_the_clips_own_audio_and_adds_no_extra_input()
    {
        var scene = VideoScene() with { AudioSource = SceneAudioSource.Clip };
        var args = FfmpegArgs.SegmentArgs(scene, "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("-map 0:v:0 -map 0:a:0", args);
        Assert.DoesNotContain("anullsrc", args);
        Assert.DoesNotContain("/voice/1.wav", args);
    }

    [Fact]
    public void Audio_mode_Clip_levels_and_pads_the_kept_audio_so_transitions_do_not_jump()
    {
        var scene = VideoScene() with { AudioSource = SceneAudioSource.Clip };
        var args = FfmpegArgs.SegmentArgs(scene, "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("dynaudnorm", args); // level-match with the TTS scenes
        Assert.Contains("apad", args);       // fill the segment - no silent gap at the cut
    }

    [Fact]
    public void Audio_mode_Silent_forces_a_generated_silent_track_and_ignores_any_voice()
    {
        var scene = VideoScene() with { AudioSource = SceneAudioSource.Silent };
        var args = FfmpegArgs.SegmentArgs(scene, "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("anullsrc", args);
        Assert.Contains("-map 0:v:0 -map 1:a:0", args);
        Assert.DoesNotContain("/voice/1.wav", args);
        Assert.DoesNotContain("dynaudnorm", args); // never normalise pure silence
    }

    [Fact]
    public void Audio_mode_Clip_on_a_still_image_degrades_to_a_silent_track()
    {
        var scene = StillScene(SceneMotion.KenBurnsIn) with { AudioSource = SceneAudioSource.Clip };
        var args = FfmpegArgs.SegmentArgs(scene, "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("anullsrc", args);
        Assert.Contains("-map 0:v:0 -map 1:a:0", args);
    }

    [Fact]
    public void A_still_segment_animates_with_zoompan()
    {
        var args = FfmpegArgs.SegmentArgs(StillScene(SceneMotion.KenBurnsIn), "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("-loop 1", args);
        Assert.Contains("zoompan", args);
        Assert.Contains("s=1080x1920", args);
    }

    [Fact]
    public void A_fade_transition_adds_a_video_fade_in()
    {
        var args = FfmpegArgs.SegmentArgs(StillScene(SceneMotion.KenBurnsIn, TransitionKind.Fade), "/out/seg.mp4", 1080, 1920, 30);

        Assert.Contains("fade=t=in:st=0", args);
    }

    [Theory]
    [InlineData(SceneMotion.KenBurnsIn, "min(zoom+0.0012,1.25)")]
    [InlineData(SceneMotion.None, "min(zoom+0.0012,1.25)")]
    [InlineData(SceneMotion.KenBurnsOut, "1.25-0.0012*on")]
    [InlineData(SceneMotion.PanLeft, "(1-on/")]
    [InlineData(SceneMotion.PanRight, "(on/")]
    [InlineData(SceneMotion.Parallax, "sin(on/")]
    public void Each_motion_produces_its_own_move(SceneMotion motion, string fragment)
    {
        var filter = FfmpegArgs.StillMotionFilter(motion, 1080, 1920, 240, 30);

        Assert.Contains(fragment, filter);
        Assert.EndsWith("setsar=1", filter);
        Assert.Contains("s=1080x1920", filter);
    }
}
