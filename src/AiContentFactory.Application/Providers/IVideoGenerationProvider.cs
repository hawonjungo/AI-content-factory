namespace AiContentFactory.Application.Providers;

/// <summary>A named visual anchor loaded from the approved asset-reference slot.</summary>
public record ReferenceImage(
    byte[] ImageBytes,
    string MimeType,
    string? Label = null,
    string? Description = null);

/// <param name="Model">
/// Overrides the provider's default model id when set - this is how the
/// Fast vs Lite tier choice (see <see cref="AiContentFactory.Domain.Generation.VideoModelTier"/>)
/// actually reaches Veo. Null keeps the configured default.
/// </param>
public record VideoGenerationRequest(
    string Prompt,
    string? NegativePrompt,
    int DurationSeconds,
    IReadOnlyList<ReferenceImage>? ReferenceImages = null,
    string AspectRatio = "9:16",
    ReferenceImage? InitialImage = null,
    string? Model = null);

public record VideoGenerationResult(byte[] VideoBytes, string MimeType, string Model);

public interface IVideoGenerationProvider
{
    /// <summary>
    /// Video generation is asynchronous upstream (long-running operation,
    /// typically 60-180s even for a short clip) - implementations poll
    /// internally and this call simply blocks until the clip is ready or
    /// the cancellation token fires. InitialImage is the required first frame
    /// for the standard pipeline's image-to-video generation.
    /// </summary>
    Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default);
}
