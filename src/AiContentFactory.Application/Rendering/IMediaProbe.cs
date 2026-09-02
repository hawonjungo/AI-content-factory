namespace AiContentFactory.Application.Rendering;

/// <param name="Ok">False when the file could not be probed at all (missing, truncated, not media).</param>
public record MediaInfo(
    bool Ok,
    string? Error,
    double DurationSeconds,
    int Width,
    int Height,
    double FrameRate,
    bool HasVideo,
    bool HasAudio,
    double AudioDurationSeconds)
{
    public static MediaInfo Unreadable(string error) => new(false, error, 0, 0, 0, 0, false, false, 0);
}

/// <summary>Reads container/stream facts from a rendered file. Implemented in Infrastructure over ffprobe.</summary>
public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string absolutePath, CancellationToken cancellationToken = default);
}
