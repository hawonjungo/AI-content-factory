using AiContentFactory.Application.Rendering;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Infrastructure.Rendering;

/// <summary>Reads container/stream facts from a rendered MP4 via <c>ffprobe -show_format -show_streams</c>.</summary>
public class FfprobeMediaProbe : IMediaProbe
{
    private const string Ffprobe = "ffprobe";

    private readonly ILogger<FfprobeMediaProbe> _logger;

    public FfprobeMediaProbe(ILogger<FfprobeMediaProbe> logger)
    {
        _logger = logger;
    }

    public async Task<MediaInfo> ProbeAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return MediaInfo.Unreadable($"file not found: {absolutePath}");
        }

        string json;
        try
        {
            json = await FfmpegProcessRunner.RunWithOutputAsync(
                Ffprobe,
                $"-v error -print_format json -show_format -show_streams \"{absolutePath}\"",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe failed for {Path}", absolutePath);
            return MediaInfo.Unreadable(ex.Message);
        }

        try
        {
            return FfprobeParser.Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse ffprobe output for {Path}", absolutePath);
            return MediaInfo.Unreadable($"unparseable ffprobe output: {ex.Message}");
        }
    }
}
