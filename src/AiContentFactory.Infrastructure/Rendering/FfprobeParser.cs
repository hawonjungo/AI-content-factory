using System.Globalization;
using System.Text.Json;
using AiContentFactory.Application.Rendering;

namespace AiContentFactory.Infrastructure.Rendering;

/// <summary>
/// Turns <c>ffprobe -print_format json -show_format -show_streams</c> output
/// into a <see cref="MediaInfo"/>. Separated from the process call so the
/// parsing is unit-testable against canned ffprobe JSON.
/// </summary>
internal static class FfprobeParser
{
    internal static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var formatDuration = 0.0;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var fd))
        {
            formatDuration = ParseDouble(fd.GetString());
        }

        var hasVideo = false;
        var hasAudio = false;
        int width = 0, height = 0;
        double frameRate = 0, audioDuration = 0, videoDuration = 0;

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
                var streamDuration = stream.TryGetProperty("duration", out var sd) ? ParseDouble(sd.GetString()) : 0;

                if (codecType == "video")
                {
                    hasVideo = true;
                    width = stream.TryGetProperty("width", out var w) && w.TryGetInt32(out var wi) ? wi : width;
                    height = stream.TryGetProperty("height", out var h) && h.TryGetInt32(out var hi) ? hi : height;

                    if (stream.TryGetProperty("avg_frame_rate", out var afr))
                    {
                        frameRate = ParseRational(afr.GetString());
                    }

                    if (frameRate <= 0 && stream.TryGetProperty("r_frame_rate", out var rfr))
                    {
                        frameRate = ParseRational(rfr.GetString());
                    }

                    videoDuration = Math.Max(videoDuration, streamDuration);
                }
                else if (codecType == "audio")
                {
                    hasAudio = true;
                    audioDuration = Math.Max(audioDuration, streamDuration);
                }
            }
        }

        var duration = formatDuration > 0 ? formatDuration : Math.Max(videoDuration, audioDuration);
        if (audioDuration <= 0 && hasAudio)
        {
            audioDuration = duration;
        }

        return new MediaInfo(
            Ok: true,
            Error: null,
            DurationSeconds: duration,
            Width: width,
            Height: height,
            FrameRate: frameRate,
            HasVideo: hasVideo,
            HasAudio: hasAudio,
            AudioDurationSeconds: audioDuration);
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    private static double ParseRational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den != 0)
        {
            return num / den;
        }

        return ParseDouble(value);
    }
}
