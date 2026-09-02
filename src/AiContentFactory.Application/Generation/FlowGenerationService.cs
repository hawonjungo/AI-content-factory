using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <param name="Tier">Fast (hero) or Lite (story beat) - decides the model id and the credit cost.</param>
/// <param name="InitialImage">First frame for true image-to-video; null for text-to-video.</param>
public record FlowClipRequest(
    Guid ContentProjectId,
    Guid SceneId,
    string Prompt,
    string? NegativePrompt,
    int DurationSeconds,
    VideoModelTier Tier,
    string AspectRatio = "9:16",
    ReferenceImage? InitialImage = null,
    IReadOnlyList<ReferenceImage>? ReferenceImages = null);

/// <param name="AlreadyGenerated">
/// True when a Completed/Validated attempt already existed for this scene, so
/// nothing was generated or charged and <see cref="VideoBytes"/> is null - the
/// caller should reuse the existing asset.
/// </param>
public record FlowClipResult(
    GenerationAttemptStatus Status,
    byte[]? VideoBytes,
    string MimeType,
    string Model,
    VideoModelTier Tier,
    int CreditsReserved,
    Guid AttemptId,
    bool AlreadyGenerated);

public class FlowGenerationException : Exception
{
    public FlowGenerationException(string message, Guid attemptId, Exception inner)
        : base(message, inner) => AttemptId = attemptId;

    public Guid AttemptId { get; }
}

/// <summary>
/// The one place AI video generation is orchestrated. It owns the
/// credit-budget handshake (reserve -> generate -> commit / fail) and the
/// Fast/Lite tier -> model mapping; the HTTP/JSON specifics of talking to Veo
/// stay behind <see cref="IVideoGenerationProvider"/>.
///
/// It does NOT assume a fixed number of clips - callers hand it exactly the
/// scenes the storyboard allocated a clip to, one call each.
///
/// Contract with the caller: on success the returned attempt is still
/// "Generating" - the caller saves the produced asset and then calls
/// <see cref="ICreditLedger.CommitAsync"/> with the new asset id and
/// <see cref="FlowClipResult.CreditsReserved"/>. On provider failure this
/// records the failure against the ledger and throws
/// <see cref="FlowGenerationException"/>.
/// </summary>
public interface IFlowGenerationService
{
    Task<FlowClipResult> GenerateClipAsync(FlowClipRequest request, CancellationToken cancellationToken = default);
}

public class FlowGenerationService : IFlowGenerationService
{
    private readonly IVideoGenerationProvider _videoProvider;
    private readonly ICreditLedger _ledger;
    private readonly CreditCostOptions _costs;
    private readonly FlowModelOptions _models;
    private readonly ILogger<FlowGenerationService> _logger;

    public FlowGenerationService(
        IVideoGenerationProvider videoProvider,
        ICreditLedger ledger,
        IOptions<CreditCostOptions> costs,
        IOptions<FlowModelOptions> models,
        ILogger<FlowGenerationService> logger)
    {
        _videoProvider = videoProvider;
        _ledger = ledger;
        _costs = costs.Value;
        _models = models.Value;
        _logger = logger;
    }

    public async Task<FlowClipResult> GenerateClipAsync(FlowClipRequest request, CancellationToken cancellationToken = default)
    {
        var model = _models.ModelFor(request.Tier);
        var credits = _costs.VideoCreditsFor(request.Tier);

        var attempt = await _ledger.ReserveAsync(
            new CreditReservationRequest(
                request.ContentProjectId,
                request.SceneId,
                GenerationKind.Video,
                _models.Provider,
                model,
                request.Tier),
            cancellationToken);

        // Idempotency: the ledger handed back a finished attempt - don't spend again.
        if (attempt.Status is GenerationAttemptStatus.Completed or GenerationAttemptStatus.Validated)
        {
            _logger.LogInformation(
                "Flow clip for scene {SceneId} already generated (attempt {AttemptId}) - reusing",
                request.SceneId, attempt.Id);
            return new FlowClipResult(attempt.Status, null, "video/mp4", model, request.Tier, 0, attempt.Id, AlreadyGenerated: true);
        }

        try
        {
            var result = await _videoProvider.GenerateAsync(
                new VideoGenerationRequest(
                    request.Prompt,
                    request.NegativePrompt,
                    request.DurationSeconds,
                    request.ReferenceImages,
                    request.AspectRatio,
                    request.InitialImage,
                    Model: model),
                cancellationToken);

            _logger.LogInformation(
                "Flow clip generated: scene {SceneId}, tier {Tier}, model {Model}, {Credits} credits reserved",
                request.SceneId, request.Tier, result.Model, credits);

            return new FlowClipResult(
                GenerationAttemptStatus.Generating,
                result.VideoBytes,
                result.MimeType,
                result.Model,
                request.Tier,
                credits,
                attempt.Id,
                AlreadyGenerated: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _ledger.FailAsync(attempt.Id, ex.Message, cancellationToken);
            throw new FlowGenerationException(
                $"Flow clip generation failed for scene {request.SceneId} ({request.Tier}): {ex.Message}", attempt.Id, ex);
        }
    }
}
