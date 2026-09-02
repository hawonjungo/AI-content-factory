using AiContentFactory.Domain.Generation;

namespace AiContentFactory.Application.Costs;

/// <summary>
/// Cost and duration assumptions used both for budget tracking and for the
/// up-front estimate the wizard shows before you spend anything.
///
/// These were previously duplicated as private consts in two services
/// (AssetGenerationService and GoogleFlowAssetGenerationService), which meant
/// the two paths could silently disagree about what a video costs. They now
/// live here and bind from the "Pricing" configuration section.
///
/// Rough estimates for planning, NOT authoritative billing - verify against
/// https://ai.google.dev/gemini-api/docs/pricing before relying on them for
/// real spend decisions.
/// </summary>
public class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>
    /// Tier-agnostic fallback per-second USD for Veo video, used only where the
    /// clip's model tier isn't known. Per-clip estimates use the tier rates below.
    /// </summary>
    public decimal VideoUsdPerSecond { get; set; } = 0.40m;

    /// <summary>
    /// Per-second USD for the Fast tier (veo-3.1-fast, with audio) - about
    /// $3.20 for an 8s clip. The user picks Fast vs Lite per clip in the build step.
    /// </summary>
    public decimal FastVideoUsdPerSecond { get; set; } = 0.40m;

    /// <summary>
    /// Per-second USD for the Lite tier (veo-3.1 lite, with audio) - about
    /// $0.64 for an 8s clip.
    /// </summary>
    public decimal LiteVideoUsdPerSecond { get; set; } = 0.08m;

    /// <summary>Per-second USD for a given video model tier - the single place tier pricing is resolved.</summary>
    public decimal VideoUsdPerSecondFor(VideoModelTier tier) => tier switch
    {
        VideoModelTier.Fast => FastVideoUsdPerSecond,
        VideoModelTier.Lite => LiteVideoUsdPerSecond,
        _ => VideoUsdPerSecond
    };

    /// <summary>
    /// Gemini 2.5 Flash Image ("Nano Banana") per generated image. This is a
    /// real, billed API call - NOT free - despite comments/UI copy elsewhere
    /// that used to claim otherwise.
    /// </summary>
    public decimal ImageUsd { get; set; } = 0.04m;

    public decimal TtsUsdPer1000Chars { get; set; } = 0.02m;

    /// <summary>
    /// Wall-clock wait per Veo text-to-video clip, NOT the clip's playback
    /// length. Clips are generated sequentially, so this number times the clip
    /// count is what the "time to build" estimate is dominated by. Tuned for
    /// veo-3.1-fast; the full model is slower.
    /// </summary>
    public int SecondsPerClipGeneration { get; set; } = 90;

    /// <summary>Wall-clock wait per Veo image-to-video clip (the Google Flow path) - faster than text-to-video.</summary>
    public int GoogleFlowSecondsPerClip { get; set; } = 60;

    public int SecondsPerImage { get; set; } = 12;

    public int SecondsPerVoiceClip { get; set; } = 8;

    public int RenderSecondsPerClip { get; set; } = 6;
}
