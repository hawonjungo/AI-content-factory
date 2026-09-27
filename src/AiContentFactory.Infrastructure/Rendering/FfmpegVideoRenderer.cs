using System.Globalization;
using AiContentFactory.Application.Rendering;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Infrastructure.Rendering;

/// <summary>
/// Shells out to the ffmpeg/ffprobe binaries (must be on PATH - installed
/// via apt in the API Dockerfile, alongside the font packages libass needs).
/// Pipeline: per-scene segment (scale/pad to target aspect ratio + camera move
/// for stills + explicit narration audio) -> concat demuxer -> styled ASS
/// caption burn -> audio mix (narration bed with ducked music, ambience, SFX,
/// fades).
///
/// Narration is the audio authority: each segment maps the scene's voice track
/// explicitly (`-map 1:a:0`) and drops the visual's own audio, which fixes the
/// bug where a Veo clip's embedded audio was auto-selected over the voice-over
/// and the finished video had captions but no narrator.
/// </summary>
public class FfmpegVideoRenderer : IVideoRenderer
{
    private const string Ffmpeg = "ffmpeg";
    private const string Ffprobe = "ffprobe";
    private const int DefaultFps = 30;

    private readonly IAudioMixingService _audioMixing;
    private readonly ILogger<FfmpegVideoRenderer> _logger;

    public FfmpegVideoRenderer(IAudioMixingService audioMixing, ILogger<FfmpegVideoRenderer> logger)
    {
        _audioMixing = audioMixing;
        _logger = logger;
    }

    public async Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
    {
        var fps = request.Fps > 0 ? request.Fps : DefaultFps;
        var workDir = Path.Combine(Path.GetTempPath(), $"render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            var segmentPaths = new List<string>();

            for (var i = 0; i < request.Scenes.Count; i++)
            {
                var scene = request.Scenes[i];
                var segmentPath = Path.Combine(workDir, $"scene_{i:D3}.mp4");
                await RenderSceneSegmentAsync(scene, segmentPath, request.Width, request.Height, fps, cancellationToken);
                segmentPaths.Add(segmentPath);
            }

            var combinedPath = Path.Combine(workDir, "combined.mp4");
            await ConcatSegmentsAsync(segmentPaths, workDir, combinedPath, cancellationToken);

            string captionedPath;
            if (request.Captions.Enabled && request.Cues.Count > 0)
            {
                var assPath = Path.Combine(workDir, "subtitles.ass");
                AssSubtitleWriter.Write(request.Cues, request.Captions, request.Width, request.Height, assPath);

                // Keep the exact subtitle script next to the finished video so a
                // caption problem can be inspected without re-deriving the cues.
                try
                {
                    File.Copy(assPath, Path.ChangeExtension(request.OutputAbsolutePath, ".subtitles.ass"), overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not save a copy of the subtitle script");
                }

                captionedPath = Path.Combine(workDir, "subtitled.mp4");
                await BurnSubtitlesAsync(combinedPath, assPath, captionedPath, cancellationToken);
            }
            else
            {
                captionedPath = combinedPath;
            }

            var finalWorkPath = Path.Combine(workDir, "final.mp4");
            await MixFinalAudioAsync(captionedPath, request, finalWorkPath, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputAbsolutePath)!);
            File.Copy(finalWorkPath, request.OutputAbsolutePath, overwrite: true);

            var duration = await GetDurationSecondsAsync(request.OutputAbsolutePath, cancellationToken);
            return new RenderResult(request.OutputAbsolutePath, duration);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up render work directory {WorkDir}", workDir); }
        }
    }

    public async Task<string> RenderCaptionPreviewAsync(CaptionPreviewRequest request, CancellationToken cancellationToken = default)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"caption-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        try
        {
            const double SampleDuration = 2.0;
            const double SampleFrameAt = 1.2;

            var assPath = Path.Combine(workDir, "preview.ass");
            AssSubtitleWriter.WriteSingleCue(request.SampleText, request.Captions, request.Width, request.Height, SampleDuration, assPath);

            var scale = $"scale={request.Width}:{request.Height}:force_original_aspect_ratio=decrease,pad={request.Width}:{request.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1";
            var filter = request.Captions.Enabled
                ? $"{scale},{SubtitlesFilter(assPath)}"
                : scale;

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputAbsolutePath)!);

            var arguments =
                $"-y -loglevel error -ss {SampleFrameAt.ToString("F2", CultureInfo.InvariantCulture)} -i \"{request.BackgroundAbsolutePath}\" " +
                $"-vf \"{filter}\" -frames:v 1 -update 1 \"{request.OutputAbsolutePath}\"";

            await FfmpegProcessRunner.RunAsync(Ffmpeg, arguments, cancellationToken);
            return request.OutputAbsolutePath;
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to clean up caption preview directory {WorkDir}", workDir); }
        }
    }

    private static Task RenderSceneSegmentAsync(RenderScene scene, string outputPath, int width, int height, int fps, CancellationToken cancellationToken) =>
        FfmpegProcessRunner.RunAsync(Ffmpeg, FfmpegArgs.SegmentArgs(scene, outputPath, width, height, fps), cancellationToken);

    private static async Task ConcatSegmentsAsync(IReadOnlyList<string> segmentPaths, string workDir, string outputPath, CancellationToken cancellationToken)
    {
        var listPath = Path.Combine(workDir, "concat_list.txt");
        var listContent = string.Join('\n', segmentPaths.Select(p => $"file '{p.Replace("'", "'\\''")}'"));
        await File.WriteAllTextAsync(listPath, listContent, cancellationToken);

        var arguments = $"-y -loglevel error -f concat -safe 0 -i \"{listPath}\" -c copy \"{outputPath}\"";
        await FfmpegProcessRunner.RunAsync(Ffmpeg, arguments, cancellationToken);
    }

    private static async Task BurnSubtitlesAsync(string inputPath, string assPath, string outputPath, CancellationToken cancellationToken)
    {
        var arguments = $"-y -loglevel error -i \"{inputPath}\" -vf \"{SubtitlesFilter(assPath)}\" -c:v libx264 -pix_fmt yuv420p -c:a copy \"{outputPath}\"";
        await FfmpegProcessRunner.RunAsync(Ffmpeg, arguments, cancellationToken);
    }

    private static string SubtitlesFilter(string assPath)
    {
        var escaped = assPath
            .Replace("\\", "/")
            .Replace(":", "\\\\:")
            .Replace(",", "\\,")
            .Replace("'", "\\'");

        return $"subtitles='{escaped}'";
    }

    private async Task MixFinalAudioAsync(string inputPath, RenderRequest request, string outputPath, CancellationToken cancellationToken)
    {
        var mix = request.AudioMix;

        // Rich mix: narration bed + ducked music + ambience + SFX + fades.
        if (mix is { HasBed: true })
        {
            var inputs = new List<string> { $"-i \"{inputPath}\"" };
            int? musicInput = null, ambienceInput = null;
            var sfxInputs = new List<int>();
            var next = 1;

            if (mix.MusicPath is not null && File.Exists(mix.MusicPath))
            {
                inputs.Add($"-i \"{mix.MusicPath}\"");
                musicInput = next++;
            }

            if (mix.AmbiencePath is not null && File.Exists(mix.AmbiencePath))
            {
                inputs.Add($"-i \"{mix.AmbiencePath}\"");
                ambienceInput = next++;
            }

            var usableSfx = new List<SfxCue>();
            foreach (var sfx in mix.Sfx)
            {
                if (File.Exists(sfx.AbsolutePath))
                {
                    inputs.Add($"-i \"{sfx.AbsolutePath}\"");
                    sfxInputs.Add(next++);
                    usableSfx.Add(sfx);
                }
            }

            var effectiveSpec = mix with { Sfx = usableSfx, MusicPath = musicInput is null ? null : mix.MusicPath, AmbiencePath = ambienceInput is null ? null : mix.AmbiencePath };

            if (effectiveSpec.HasBed)
            {
                var graph = _audioMixing.BuildFilterGraph(effectiveSpec, new AudioInputLayout(0, musicInput, ambienceInput, sfxInputs));
                var arguments =
                    $"-y -loglevel error {string.Join(' ', inputs)} " +
                    $"-filter_complex \"{graph.FilterComplex}\" -map 0:v -map \"{graph.OutLabel}\" " +
                    $"-c:v copy -c:a aac -ar 48000 -ac 2 -shortest \"{outputPath}\"";
                await FfmpegProcessRunner.RunAsync(Ffmpeg, arguments, cancellationToken);
                return;
            }
        }

        // Legacy fallback: a single background music track at a fixed low volume.
        if (!string.IsNullOrWhiteSpace(request.BackgroundMusicAbsolutePath) && File.Exists(request.BackgroundMusicAbsolutePath))
        {
            var filter = "[1:a]volume=0.15,aloop=loop=-1:size=2e9[music];[0:a][music]amix=inputs=2:duration=first:dropout_transition=2[aout]";
            var arguments = $"-y -loglevel error -i \"{inputPath}\" -i \"{request.BackgroundMusicAbsolutePath}\" -filter_complex \"{filter}\" -map 0:v -map \"[aout]\" -c:v copy -c:a aac -shortest \"{outputPath}\"";
            await FfmpegProcessRunner.RunAsync(Ffmpeg, arguments, cancellationToken);
            return;
        }

        File.Copy(inputPath, outputPath, overwrite: true);
    }

    private static async Task<double> GetDurationSecondsAsync(string filePath, CancellationToken cancellationToken)
    {
        var arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";
        var output = await FfmpegProcessRunner.RunWithOutputAsync(Ffprobe, arguments, cancellationToken);
        return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) ? duration : 0;
    }
}
