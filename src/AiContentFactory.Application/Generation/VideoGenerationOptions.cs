namespace AiContentFactory.Application.Generation;

/// <summary>
/// Bound from the "Llm:VideoGeneration" configuration section. Only the pieces
/// the application layer actually branches on live here; the Google Flow
/// credit/quota numbers stay with the infrastructure quota manager.
/// </summary>
public class VideoGenerationOptions
{
    public const string SectionName = "Llm:VideoGeneration";

    /// <summary>
    /// Whether the wizard offers "dựng bằng Google Flow" as a choice. When
    /// false the toggle is hidden and only the standard path runs.
    /// </summary>
    public bool GoogleFlowEnabled { get; set; } = true;

    /// <summary>
    /// Whether the configured video model accepts an image as the first frame
    /// (true image-to-video). `veo-3.1-fast-generate-preview` does NOT - it is
    /// text-to-video only and 400s on any inlineData - so this defaults false.
    /// When false the standard pipeline skips generating a per-clip anchor
    /// image (a wasted image call for a text-only model) and relies on the
    /// text prompt for character/environment consistency.
    /// </summary>
    public bool ImageToVideoEnabled { get; set; }
}
