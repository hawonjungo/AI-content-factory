using System.Diagnostics;
using System.Globalization;
using AiContentFactory.Application.Rendering;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Infrastructure.Rendering;

/// <summary>
/// <see cref="IVideoFrameExtractor"/> on the FFmpeg CLI. Arguments go through
/// <see cref="ProcessStartInfo.ArgumentList"/> (never a shell string) and the
/// process is killed if it outlives <see cref="Timeout"/>.
/// </summary>
public class FfmpegFrameExtractor : IVideoFrameExtractor
{
    private const string Ffmpeg = "ffmpeg";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ILogger<FfmpegFrameExtractor> _logger;

    public FfmpegFrameExtractor(ILogger<FfmpegFrameExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<byte[]> ExtractFrameAsync(string absoluteVideoPath, double? atSeconds, CancellationToken cancellationToken = default, int? maxHeight = null)
    {
        if (!File.Exists(absoluteVideoPath))
        {
            throw new InvalidOperationException("The video file to extract a frame from does not exist.");
        }

        var output = Path.Combine(Path.GetTempPath(), $"acf-frame-{Guid.NewGuid():N}.png");
        var psi = new ProcessStartInfo
        {
            FileName = Ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        if (atSeconds is { } seconds)
        {
            psi.ArgumentList.Add("-ss");
            psi.ArgumentList.Add(Math.Max(0, seconds).ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(absoluteVideoPath);
            psi.ArgumentList.Add("-frames:v");
            psi.ArgumentList.Add("1");
        }
        else
        {
            // Seek near the end, decode the remaining frames and keep overwriting
            // the output - what is left is the true last frame.
            psi.ArgumentList.Add("-sseof");
            psi.ArgumentList.Add("-0.5");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(absoluteVideoPath);
            psi.ArgumentList.Add("-update");
            psi.ArgumentList.Add("1");
        }

        if (maxHeight is > 0 and var height)
        {
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add($"scale=-2:'min({height},ih)'");
        }

        psi.ArgumentList.Add(output);

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            if (process.ExitCode != 0 || !File.Exists(output))
            {
                _logger.LogWarning("ffmpeg frame extraction failed with exit code {ExitCode}: {Stderr}", process.ExitCode, await stderrTask);
                throw new InvalidOperationException("Could not extract a frame from the video.");
            }

            var bytes = await File.ReadAllBytesAsync(output, cancellationToken);
            if (bytes.Length == 0)
            {
                throw new InvalidOperationException("Could not extract a frame from the video.");
            }

            return bytes;
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already exited
        }
    }
}
