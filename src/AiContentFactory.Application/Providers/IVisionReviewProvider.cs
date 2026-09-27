namespace AiContentFactory.Application.Providers;

/// <param name="Label">Short caption placed right before the image so the model knows what it is ("Reference: Milo", "Clip frame 2 of 3").</param>
public record VisionImage(byte[] Bytes, string MimeType, string Label);

/// <param name="Instruction">System instruction (role, rules, required JSON shape).</param>
/// <param name="Question">The per-request text (what the clip is supposed to show).</param>
public record VisionReviewRequest(string Instruction, string Question, IReadOnlyList<VisionImage> Images);

/// <param name="Json">The model's raw JSON answer - parsed by the caller.</param>
public record VisionReviewResult(string Json, string Model);

/// <summary>
/// The replaceable seam for "look at these images and answer in JSON" - a
/// billable multimodal call. Implementations must make exactly ONE provider
/// request per call (never retry on their own: the caller decides whether a
/// paid call is repeated).
/// </summary>
public interface IVisionReviewProvider
{
    bool IsConfigured { get; }

    Task<VisionReviewResult> ReviewAsync(VisionReviewRequest request, CancellationToken cancellationToken = default);
}
