namespace AiContentFactory.Application.Providers;

/// <param name="ReferenceImages">
/// Approved character/environment references to anchor the output to (style
/// transfer). Gemini's image model accepts these as extra image parts; a
/// provider that can't use them just ignores the list.
/// </param>
public record ImageGenerationRequest(
    string Prompt,
    string? NegativePrompt,
    IReadOnlyList<ReferenceImage>? ReferenceImages = null);

public record ImageGenerationResult(byte[] ImageBytes, string MimeType, string Model);

public interface IImageGenerationProvider
{
    Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);
}
