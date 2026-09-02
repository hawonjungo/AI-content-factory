using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Generation;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Generation;

/// <param name="References">Approved character/environment anchors to keep the still consistent with the rest of the storyboard.</param>
/// <param name="Purpose">Free-text note recorded for traceability ("scene still", "scene anchor for image-to-video").</param>
public record SceneImageRequest(
    Guid ContentProjectId,
    Guid SceneId,
    string Prompt,
    string? NegativePrompt,
    IReadOnlyList<ReferenceImage>? References = null,
    string Purpose = "scene still");

public record SceneImageResult(
    GenerationAttemptStatus Status,
    byte[]? ImageBytes,
    string MimeType,
    string Model,
    int CreditsReserved,
    Guid AttemptId,
    bool AlreadyGenerated);

/// <summary>
/// Treats AI images as first-class storyboard assets rather than throwaway
/// intermediates: every image generation is budget-checked, recorded as a
/// <see cref="GenerationAttempt"/>, and de-duplicated - a scene that already
/// has a completed image attempt is not regenerated. Continuity references are
/// folded into the prompt so stills match the clips around them, and the same
/// call is used whether the image is a final scene visual or the anchor frame
/// for image-to-video.
///
/// Same caller contract as <see cref="IFlowGenerationService"/>: on success the
/// attempt is left "Generating" for the caller to commit once the asset is
/// saved; on provider failure the failure is recorded and the exception rethrown.
/// </summary>
public interface IImageGenerationService
{
    Task<SceneImageResult> GenerateSceneImageAsync(SceneImageRequest request, CancellationToken cancellationToken = default);
}

public class ImageGenerationService : IImageGenerationService
{
    private const string Provider = "gemini-image";

    private readonly IImageGenerationProvider _imageProvider;
    private readonly ICreditLedger _ledger;
    private readonly ILogger<ImageGenerationService> _logger;

    public ImageGenerationService(
        IImageGenerationProvider imageProvider,
        ICreditLedger ledger,
        ILogger<ImageGenerationService> logger)
    {
        _imageProvider = imageProvider;
        _ledger = ledger;
        _logger = logger;
    }

    public async Task<SceneImageResult> GenerateSceneImageAsync(SceneImageRequest request, CancellationToken cancellationToken = default)
    {
        var attempt = await _ledger.ReserveAsync(
            new CreditReservationRequest(
                request.ContentProjectId,
                request.SceneId,
                GenerationKind.Image,
                Provider,
                "image",
                Tier: null),
            cancellationToken);

        if (attempt.Status is GenerationAttemptStatus.Completed or GenerationAttemptStatus.Validated)
        {
            _logger.LogInformation(
                "Scene image for {SceneId} already generated (attempt {AttemptId}) - reusing, no new image call",
                request.SceneId, attempt.Id);
            return new SceneImageResult(attempt.Status, null, "image/png", "image", 0, attempt.Id, AlreadyGenerated: true);
        }

        try
        {
            var result = await _imageProvider.GenerateAsync(
                new ImageGenerationRequest(
                    ComposePrompt(request),
                    request.NegativePrompt,
                    request.References),
                cancellationToken);

            return new SceneImageResult(
                GenerationAttemptStatus.Generating,
                result.ImageBytes,
                result.MimeType,
                result.Model,
                attempt.EstimatedCredits,
                attempt.Id,
                AlreadyGenerated: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _ledger.FailAsync(attempt.Id, ex.Message, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Adds cinematic-composition and continuity direction so a still reads as
    /// part of the same piece as the clips around it.
    /// </summary>
    private static string ComposePrompt(SceneImageRequest request)
    {
        var lines = new List<string>();

        if (request.References is { Count: > 0 })
        {
            lines.Add("Match the character identity, palette, lighting and environment of the attached reference image(s) exactly.");
        }

        lines.Add("Cinematic 9:16 vertical composition: deliberate framing, clear focal subject, depth, filmic lighting.");
        lines.Add($"Scene: {request.Prompt}");
        return string.Join("\n", lines);
    }
}
