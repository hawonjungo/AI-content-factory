using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;
using Xunit;

namespace AiContentFactory.Tests;

public class SceneAudioResolverTests
{
    // ---- Smart / Auto: the per-clip matrix from the spec -------------------

    [Theory]
    // media,           isStill, clipAudio, narration -> expected source
    [InlineData(false, true, true, SceneAudioSource.Clip)]    // video + original audio (+narration) -> keep clip audio
    [InlineData(false, true, false, SceneAudioSource.Clip)]   // video + original audio, no narration -> keep clip audio
    [InlineData(true, false, true, SceneAudioSource.Voice)]   // image + narration -> TTS
    [InlineData(false, false, true, SceneAudioSource.Voice)]  // video, no audio, + narration -> TTS
    [InlineData(true, false, false, SceneAudioSource.Silent)] // image, no narration -> silent
    [InlineData(false, false, false, SceneAudioSource.Silent)]// video, no audio, no narration -> silent
    public void Smart_resolves_audio_per_clip(bool isStill, bool clipAudio, bool narration, SceneAudioSource expected)
    {
        var d = SceneAudioResolver.Resolve(AudioMode.Smart, isStill, clipAudio, narration);
        Assert.Equal(expected, d.Source);
        Assert.Equal(expected == SceneAudioSource.Voice, d.NeedsTts);
    }

    [Fact]
    public void Smart_the_acceptance_scenario_flow_video_image_flow_video_image()
    {
        // Clip 1: Flow video + original voice   -> original
        // Clip 2: Image + narration             -> TTS
        // Clip 3: Flow video + original voice   -> original
        // Clip 4: Image + narration             -> TTS
        var clip1 = SceneAudioResolver.Resolve(AudioMode.Smart, isStillImage: false, clipHasAudioStream: true, hasNarration: true);
        var clip2 = SceneAudioResolver.Resolve(AudioMode.Smart, isStillImage: true, clipHasAudioStream: false, hasNarration: true);
        var clip3 = SceneAudioResolver.Resolve(AudioMode.Smart, isStillImage: false, clipHasAudioStream: true, hasNarration: true);
        var clip4 = SceneAudioResolver.Resolve(AudioMode.Smart, isStillImage: true, clipHasAudioStream: false, hasNarration: true);

        Assert.Equal(SceneAudioSource.Clip, clip1.Source);
        Assert.Equal(SceneAudioSource.Voice, clip2.Source);
        Assert.Equal(SceneAudioSource.Clip, clip3.Source);
        Assert.Equal(SceneAudioSource.Voice, clip4.Source);

        // No duplicate / overlapping narration: TTS is requested only for the
        // two image clips, never for the two clips that already have voice.
        Assert.False(clip1.NeedsTts);
        Assert.True(clip2.NeedsTts);
        Assert.False(clip3.NeedsTts);
        Assert.True(clip4.NeedsTts);
    }

    [Fact]
    public void Smart_keeps_a_clip_at_its_own_length_and_lets_TTS_scenes_follow_narration()
    {
        var kept = SceneAudioResolver.Resolve(AudioMode.Smart, false, clipHasAudioStream: true, hasNarration: true);
        var tts = SceneAudioResolver.Resolve(AudioMode.Smart, true, clipHasAudioStream: false, hasNarration: true);

        Assert.True(kept.UseClipDuration);    // Clip scene = clip length
        Assert.False(tts.UseClipDuration);    // TTS scene = narration length
    }

    [Theory]
    [InlineData(false, true, true, SceneAudioSource.Clip)]    // video + original audio -> keep it
    [InlineData(true, false, true, SceneAudioSource.Voice)]   // image + narration -> AI voice fallback
    [InlineData(false, false, true, SceneAudioSource.Voice)]  // video, no audio, + narration -> AI voice fallback
    [InlineData(true, false, false, SceneAudioSource.Silent)] // image, no narration -> silent
    public void Original_audio_mode_resolves_the_same_way_as_smart(bool isStill, bool clipAudio, bool narration, SceneAudioSource expected)
    {
        var original = SceneAudioResolver.Resolve(AudioMode.Original, isStill, clipAudio, narration);
        var smart = SceneAudioResolver.Resolve(AudioMode.Smart, isStill, clipAudio, narration);

        Assert.Equal(expected, original.Source);
        Assert.Equal(smart, original);
    }

    // ---- Generate new voice: unchanged pipeline ---------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generated_always_uses_the_voice_bed_and_never_a_clip_duration(bool clipAudio)
    {
        var narrated = SceneAudioResolver.Resolve(AudioMode.Generated, isStillImage: false, clipAudio, hasNarration: true);
        var silentScene = SceneAudioResolver.Resolve(AudioMode.Generated, isStillImage: false, clipAudio, hasNarration: false);

        Assert.Equal(SceneAudioSource.Voice, narrated.Source);
        Assert.True(narrated.NeedsTts);
        Assert.False(narrated.UseClipDuration);

        Assert.Equal(SceneAudioSource.Voice, silentScene.Source);
        Assert.False(silentScene.NeedsTts);
    }

    // ---- Mute -----------------------------------------------------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Mute_is_always_silent_and_never_needs_tts(bool isStill, bool narration)
    {
        var d = SceneAudioResolver.Resolve(AudioMode.Muted, isStill, clipHasAudioStream: true, narration);

        Assert.Equal(SceneAudioSource.Silent, d.Source);
        Assert.False(d.NeedsTts);
        Assert.Equal(!isStill, d.UseClipDuration); // a video scene still lasts its own length
    }
}
