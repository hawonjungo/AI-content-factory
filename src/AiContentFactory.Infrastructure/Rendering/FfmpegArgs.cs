using System.Globalization;
using AiContentFactory.Application.Rendering;

namespace AiContentFactory.Infrastructure.Rendering;

/// <summary>
/// Pure ffmpeg argument assembly, split out from <see cref="FfmpegVideoRenderer"/>
/// so the tricky parts - the explicit narration audio map, the still-image
/// camera moves - are unit-testable without a real ffmpeg binary.
/// </summary>
internal static class FfmpegArgs
{
    internal const double FadeSeconds = 0.25;

    /// <summary>
    /// dynaudnorm keeps the kept-original-audio clips and the TTS voice-over at a
    /// consistent perceived level, so the audio never "jumps" at a video→image
    /// transition. apad then fills the segment to its full timeline length so
    /// there is no silent gap where one clip's audio ends before the cut. Both
    /// only apply to real audio - normalising pure silence would raise its noise
    /// floor.
    /// </summary>
    internal const string LevelledAudioFilter = "aresample=24000,dynaudnorm=f=200:g=15:p=0.9:m=8,apad";

    /// <summary>
    /// One scene segment. Input 0 is always the visual. The audio map depends on
    /// the Step 6 Voice option (<see cref="RenderScene.AudioSource"/>):
    /// <list type="bullet">
    /// <item><c>Voice</c> - input 1 is the narration track (or generated silence);
    /// mapped <c>-map 1:a:0</c> so a clip's own audio can't be picked over the voice-over.</item>
    /// <item><c>Clip</c> - the clip's own embedded audio is kept: <c>-map 0:a:0</c>, no extra input.</item>
    /// <item><c>Silent</c> - input 1 is generated silence; <c>-map 1:a:0</c>.</item>
    /// </list>
    /// Every branch produces exactly <c>duration</c> seconds of aac/24000/mono
    /// audio, so the concat demuxer copies the segments into one continuous,
    /// level-matched audio timeline.
    /// </summary>
    internal static string SegmentArgs(RenderScene scene, string outputPath, int width, int height, int fps)
    {
        var duration = scene.DurationSeconds.ToString("F2", CultureInfo.InvariantCulture);

        // A still image never has usable audio, so "keep clip audio" degrades to silence there.
        var audioSource = scene.AudioSource == SceneAudioSource.Clip && scene.IsStillImage
            ? SceneAudioSource.Silent
            : scene.AudioSource;

        var silenceInput = $"-f lavfi -t {duration} -i anullsrc=channel_layout=mono:sample_rate=24000";

        var (audioInput, audioMap, audioFilter) = audioSource switch
        {
            SceneAudioSource.Clip => (string.Empty, "-map 0:a:0", LevelledAudioFilter),
            SceneAudioSource.Silent => (silenceInput, "-map 1:a:0", string.Empty),
            _ => scene.VoiceAbsolutePath is not null
                ? ($"-i \"{scene.VoiceAbsolutePath}\"", "-map 1:a:0", LevelledAudioFilter)
                : (silenceInput, "-map 1:a:0", string.Empty),
        };

        string visualInput;
        string filter;

        if (scene.IsStillImage)
        {
            var frames = Math.Max(1, (int)Math.Round(scene.DurationSeconds * fps));
            visualInput = $"-loop 1 -i \"{scene.VisualAbsolutePath}\"";
            filter = StillMotionFilter(scene.Motion, width, height, frames, fps);
        }
        else
        {
            visualInput = $"-stream_loop -1 -i \"{scene.VisualAbsolutePath}\"";
            filter = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1";
        }

        if (scene.TransitionIn == TransitionKind.Fade)
        {
            filter += $",fade=t=in:st=0:d={FadeSeconds.ToString("0.##", CultureInfo.InvariantCulture)}";
        }

        var inputs = string.IsNullOrEmpty(audioInput) ? visualInput : $"{visualInput} {audioInput}";
        var afArg = string.IsNullOrEmpty(audioFilter) ? string.Empty : $"-af \"{audioFilter}\" ";

        // -t is the single authority for the segment length (video is looped,
        // audio is apad-padded), so -shortest is intentionally not used - it
        // would let a voice track shorter than the scene cut the segment early
        // and drift every later scene and caption.
        return
            $"-y -loglevel error {inputs} -map 0:v:0 {audioMap} -t {duration} -r {fps} -vf \"{filter}\" {afArg}" +
            $"-c:v libx264 -pix_fmt yuv420p -c:a aac -ar 24000 -ac 1 \"{outputPath}\"";
    }

    /// <summary>
    /// zoompan / crop expression giving a still scene a camera move so it never
    /// reads as a static slide. Every branch ends pinned to the target size + SAR.
    /// </summary>
    internal static string StillMotionFilter(SceneMotion motion, int width, int height, int frames, int fps)
    {
        var prescale = $"scale={width * 2}:{height * 2}:force_original_aspect_ratio=increase,crop={width * 2}:{height * 2}";
        var size = $"s={width}x{height}:fps={fps}:d={frames}";
        var centreX = "x='iw/2-(iw/zoom/2)'";
        var centreY = "y='ih/2-(ih/zoom/2)'";
        var f = Math.Max(1, frames);

        var zoompan = motion switch
        {
            SceneMotion.KenBurnsOut =>
                $"zoompan=z='max(1.0,1.25-0.0012*on)':{centreX}:{centreY}:{size}",
            SceneMotion.PanLeft =>
                $"zoompan=z=1.15:x='(iw-iw/zoom)*(1-on/{f})':{centreY}:{size}",
            SceneMotion.PanRight =>
                $"zoompan=z=1.15:x='(iw-iw/zoom)*(on/{f})':{centreY}:{size}",
            SceneMotion.Parallax =>
                $"zoompan=z='min(zoom+0.0010,1.30)':x='iw/2-(iw/zoom/2)+sin(on/{f}*3.14159)*40':{centreY}:{size}",
            _ =>
                $"zoompan=z='min(zoom+0.0012,1.25)':{centreX}:{centreY}:{size}",
        };

        return $"{prescale},{zoompan},pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1";
    }
}
