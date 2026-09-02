using AiContentFactory.Application.Rendering;

namespace AiContentFactory.Application.Publishing;

/// <summary>
/// The platform-agnostic checks every short-form destination shares: it must be
/// a real MP4, portrait 9:16, and have a non-zero duration. Per-platform limits
/// (duration ceiling, size ceiling) are the publisher's job - this deliberately
/// does NOT hard-code a universal 60s cap.
/// </summary>
public static class PublishVideoValidator
{
    /// <summary>9:16 = 0.5625. Allow a little slack for 1080x1920 vs 1088x1920 encoder rounding.</summary>
    public const double TargetAspect = 9.0 / 16.0;
    public const double AspectTolerance = 0.03;

    public static IReadOnlyList<string> Validate(string? storageKey, MediaInfo probe, long sizeBytes)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            errors.Add("Chưa có video hoàn chỉnh để đăng.");
            return errors;
        }

        if (!storageKey.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Video hoàn chỉnh phải là tệp .mp4.");
        }

        if (!probe.Ok || !probe.HasVideo)
        {
            errors.Add($"Không đọc được luồng video ({probe.Error ?? "no video stream"}).");
            return errors;
        }

        if (probe.DurationSeconds <= 0)
        {
            errors.Add("Video có thời lượng 0 giây.");
        }

        if (probe.Width > 0 && probe.Height > 0)
        {
            var aspect = (double)probe.Width / probe.Height;
            if (Math.Abs(aspect - TargetAspect) > AspectTolerance)
            {
                errors.Add($"Video phải là dọc 9:16 (hiện tại {probe.Width}x{probe.Height}).");
            }
        }
        else
        {
            errors.Add("Không đọc được độ phân giải video.");
        }

        if (sizeBytes <= 0)
        {
            errors.Add("Tệp video rỗng hoặc không tồn tại.");
        }

        return errors;
    }
}
