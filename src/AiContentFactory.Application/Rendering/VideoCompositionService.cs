using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Rendering;

/// <param name="StoryboardSceneCount">Total scenes the storyboard has - used to detect a missing-scene gap.</param>
/// <param name="ScenesWithVisual">Scenes that had a ready visual asset. Less than the count above = a gap.</param>
public record CompositionRequest(
    Guid ContentProjectId,
    Timeline Timeline,
    CaptionSettings Captions,
    string OutputAbsolutePath,
    string? BackgroundMusicAbsolutePath,
    int StoryboardSceneCount,
    int ScenesWithVisual,
    int Width = 1080,
    int Height = 1920,
    int Fps = 30);

public record CompositionResult(string OutputAbsolutePath, double DurationSeconds, VideoValidationReport Validation);

public class VideoCompositionException : Exception
{
    public VideoCompositionException(VideoValidationReport report)
        : base(report.Summary) => Report = report;

    public VideoValidationReport Report { get; }
}

/// <summary>
/// Turns a <see cref="Timeline"/> into a finished MP4 and refuses to hand it
/// back unless it passes <see cref="IVideoQualityValidator"/>. Composition
/// reuses the existing FFmpeg renderer; this service is the audio-first
/// orchestration and the final gate around it.
/// </summary>
public interface IVideoCompositionService
{
    Task<CompositionResult> ComposeAsync(CompositionRequest request, CancellationToken cancellationToken = default);
}

public class VideoCompositionService : IVideoCompositionService
{
    private const double DurationToleranceSeconds = 3.0;
    private const double HardMinSeconds = 20.0;
    private const double HardMaxSeconds = 120.0;

    private readonly IVideoRenderer _renderer;
    private readonly IVideoQualityValidator _validator;
    private readonly ILogger<VideoCompositionService> _logger;

    public VideoCompositionService(
        IVideoRenderer renderer,
        IVideoQualityValidator validator,
        ILogger<VideoCompositionService> logger)
    {
        _renderer = renderer;
        _validator = validator;
        _logger = logger;
    }

    public async Task<CompositionResult> ComposeAsync(CompositionRequest request, CancellationToken cancellationToken = default)
    {
        var timeline = request.Timeline;

        var renderScenes = timeline.Scenes
            .OrderBy(s => s.StartSeconds)
            .Select(s => new RenderScene(
                s.VisualAbsolutePath,
                s.VoiceAbsolutePath,
                s.DurationSeconds,
                Narration: string.Empty,
                s.IsStillImage,
                s.Motion,
                s.TransitionIn))
            .ToList();

        var renderRequest = new RenderRequest(
            renderScenes,
            request.BackgroundMusicAbsolutePath,
            request.OutputAbsolutePath,
            request.Captions,
            timeline.AllCues,
            request.Width,
            request.Height,
            request.Fps,
            timeline.Audio);

        var renderSucceeded = true;
        RenderResult render;
        try
        {
            render = await _renderer.RenderAsync(renderRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Composition render failed for project {ProjectId}", request.ContentProjectId);
            renderSucceeded = false;
            render = new RenderResult(request.OutputAbsolutePath, 0);
        }

        var narrationSeconds = timeline.Scenes.Where(s => s.HasNarration).Sum(s => s.DurationSeconds);

        var context = new VideoValidationContext(
            OutputAbsolutePath: request.OutputAbsolutePath,
            TargetWidth: request.Width,
            TargetHeight: request.Height,
            TargetFps: request.Fps,
            ExpectedMinSeconds: Math.Max(HardMinSeconds, timeline.TotalSeconds - DurationToleranceSeconds),
            ExpectedMaxSeconds: Math.Min(HardMaxSeconds, timeline.TotalSeconds + DurationToleranceSeconds),
            NarrationSeconds: narrationSeconds,
            CaptionsEnabled: request.Captions.Enabled,
            Cues: timeline.AllCues,
            SceneCount: request.StoryboardSceneCount,
            ScenesWithVisual: request.ScenesWithVisual,
            RenderSucceeded: renderSucceeded);

        var report = await _validator.ValidateAsync(context, cancellationToken);

        if (!timeline.WithinTargetRange)
        {
            _logger.LogWarning(
                "Timeline total {Total:0.0}s is outside the {Min:0.0}-{Max:0.0}s target for project {ProjectId}",
                timeline.TotalSeconds, timeline.TargetMinSeconds, timeline.TargetMaxSeconds, request.ContentProjectId);
        }

        if (!report.IsValid)
        {
            _logger.LogError("Final video validation failed for project {ProjectId}: {Errors}",
                request.ContentProjectId, string.Join("; ", report.Errors));
            throw new VideoCompositionException(report);
        }

        foreach (var warning in report.Warnings)
        {
            _logger.LogWarning("Final video validation warning for project {ProjectId}: {Warning}", request.ContentProjectId, warning);
        }

        return new CompositionResult(render.OutputAbsolutePath, render.DurationSeconds, report);
    }
}
