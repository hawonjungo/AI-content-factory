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
    /// One scene segment: input 0 is the visual, input 1 is the narration audio
    /// (or generated silence). The maps are explicit - <c>-map 0:v:0 -map 1:a:0</c>
    /// - so a video clip's own audio track can never be picked over the voice-over.
    /// </summary>
    internal static string SegmentArgs(RenderScene scene, string outputPath, int width, int height, int fps)
    {
        var duration = scene.DurationSeconds.ToString("F2", CultureInfo.InvariantCulture);

        var audioInput = scene.VoiceAbsolutePath is not null
            ? $"-i \"{scene.VoiceAbsolutePath}\""
            : $"-f lavfi -t {duration} -i anullsrc=channel_layout=mono:sample_rate=24000";

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

        return
            $"-y -loglevel error {visualInput} {audioInput} -map 0:v:0 -map 1:a:0 -t {duration} -r {fps} -vf \"{filter}\" " +
            $"-c:v libx264 -pix_fmt yuv420p -c:a aac -ar 24000 -ac 1 -shortest \"{outputPath}\"";
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
