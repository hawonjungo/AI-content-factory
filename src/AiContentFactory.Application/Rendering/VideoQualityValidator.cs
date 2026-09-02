namespace AiContentFactory.Application.Rendering;

/// <param name="ExpectedMinSeconds">Lower bound for the finished video (typically the timeline total minus a tolerance).</param>
/// <param name="ExpectedMaxSeconds">Upper bound for the finished video.</param>
/// <param name="ScenesWithVisual">Scenes that actually had a ready visual asset; less than <see cref="SceneCount"/> means a gap.</param>
public record VideoValidationContext(
    string OutputAbsolutePath,
    int TargetWidth,
    int TargetHeight,
    int TargetFps,
    double ExpectedMinSeconds,
    double ExpectedMaxSeconds,
    double NarrationSeconds,
    bool CaptionsEnabled,
    IReadOnlyList<CaptionCue> Cues,
    int SceneCount,
    int ScenesWithVisual,
    bool RenderSucceeded);

public record VideoValidationReport(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public string Summary => IsValid
        ? "video passed final validation"
        : "video failed final validation: " + string.Join("; ", Errors);
}

public interface IVideoQualityValidator
{
    /// <summary>
    /// The gate before a project may become AwaitingApproval. Returns every
    /// problem found (not just the first) so the failure is actionable.
    /// </summary>
    Task<VideoValidationReport> ValidateAsync(VideoValidationContext context, CancellationToken cancellationToken = default);
}

public class VideoQualityValidator : IVideoQualityValidator
{
    private const double AspectTolerance = 0.02;   // 9:16 = 0.5625
    private const double SyncToleranceSeconds = 1.5;
    private const double FpsTolerance = 1.0;
    private const double CaptionTailToleranceSeconds = 1.0;

    private readonly IMediaProbe _probe;

    public VideoQualityValidator(IMediaProbe probe)
    {
        _probe = probe;
    }

    public async Task<VideoValidationReport> ValidateAsync(VideoValidationContext context, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!context.RenderSucceeded)
        {
            errors.Add("render did not complete successfully");
        }

        if (context.ScenesWithVisual < context.SceneCount)
        {
            errors.Add($"{context.SceneCount - context.ScenesWithVisual} of {context.SceneCount} scene(s) have no visual asset");
        }

        if (context.NarrationSeconds <= 0)
        {
            errors.Add("no narration audio was produced for this video");
        }

        ValidateCaptions(context, errors);

        if (string.IsNullOrWhiteSpace(context.OutputAbsolutePath) || !File.Exists(context.OutputAbsolutePath))
        {
            errors.Add($"output file not found at '{context.OutputAbsolutePath}'");
            return new VideoValidationReport(false, errors, warnings);
        }

        var media = await _probe.ProbeAsync(context.OutputAbsolutePath, cancellationToken);
        if (!media.Ok)
        {
            errors.Add($"output is not a readable MP4 ({media.Error ?? "unknown probe error"})");
            return new VideoValidationReport(false, errors, warnings);
        }

        if (!media.HasVideo)
        {
            errors.Add("output has no video stream");
        }

        if (!media.HasAudio)
        {
            errors.Add("output has no audio stream - narrator audio is missing from the final video");
        }
        else if (media.AudioDurationSeconds <= 0)
        {
            errors.Add("output audio stream has zero duration");
        }

        if (media.Width > 0 && media.Height > 0)
        {
            var ratio = (double)media.Width / media.Height;
            if (Math.Abs(ratio - 9.0 / 16.0) > AspectTolerance)
            {
                errors.Add($"output aspect ratio is {media.Width}x{media.Height} ({ratio:0.###}), expected 9:16 (0.5625)");
            }

            if (media.Width != context.TargetWidth || media.Height != context.TargetHeight)
            {
                errors.Add($"output resolution is {media.Width}x{media.Height}, expected {context.TargetWidth}x{context.TargetHeight}");
            }
        }
        else
        {
            errors.Add("output resolution could not be determined");
        }

        if (media.DurationSeconds < context.ExpectedMinSeconds || media.DurationSeconds > context.ExpectedMaxSeconds)
        {
            errors.Add($"final duration {media.DurationSeconds:0.0}s is outside the expected {context.ExpectedMinSeconds:0.0}-{context.ExpectedMaxSeconds:0.0}s range");
        }

        if (media.HasAudio && media.AudioDurationSeconds > 0 &&
            Math.Abs(media.DurationSeconds - media.AudioDurationSeconds) > SyncToleranceSeconds)
        {
            errors.Add($"audio and video are out of sync by {Math.Abs(media.DurationSeconds - media.AudioDurationSeconds):0.0}s " +
                       $"(video {media.DurationSeconds:0.0}s, audio {media.AudioDurationSeconds:0.0}s)");
        }

        if (media.FrameRate > 0 && Math.Abs(media.FrameRate - context.TargetFps) > FpsTolerance)
        {
            warnings.Add($"frame rate is {media.FrameRate:0.##}, expected {context.TargetFps}");
        }

        if (context.CaptionsEnabled && context.Cues.Count > 0 && media.DurationSeconds > 0)
        {
            var lastCueEnd = context.Cues.Max(c => c.EndSeconds);
            if (lastCueEnd > media.DurationSeconds + CaptionTailToleranceSeconds)
            {
                errors.Add($"last caption ends at {lastCueEnd:0.0}s, past the {media.DurationSeconds:0.0}s video");
            }
        }

        return new VideoValidationReport(errors.Count == 0, errors, warnings);
    }

    private static void ValidateCaptions(VideoValidationContext context, List<string> errors)
    {
        if (!context.CaptionsEnabled)
        {
            return;
        }

        if (context.Cues.Count == 0)
        {
            errors.Add("captions are enabled but no caption cues were generated");
            return;
        }

        var ordered = context.Cues.OrderBy(c => c.StartSeconds).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].EndSeconds <= ordered[i].StartSeconds)
            {
                errors.Add($"caption cue {i + 1} has non-positive duration ({ordered[i].StartSeconds:0.00}-{ordered[i].EndSeconds:0.00}s)");
                return;
            }

            if (i > 0 && ordered[i].StartSeconds < ordered[i - 1].EndSeconds - 0.05)
            {
                errors.Add($"caption cues {i} and {i + 1} overlap");
                return;
            }
        }
    }
}
