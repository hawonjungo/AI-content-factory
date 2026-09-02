namespace AiContentFactory.Application.Generation;

/// <summary>
/// Bound from "Llm:VideoGeneration:GoogleFlow". Google's Flow tier grants a
/// pool of free credits per UTC day; this is how many, and what each kind of
/// generation draws from it. Shown in the estimate as reference info only -
/// generation is no longer stopped by it (Veo bills real USD per second, see
/// <see cref="PricingOptions.VideoUsdPerSecond"/>).
/// </summary>
public class GoogleFlowOptions
{
    public const string SectionName = "Llm:VideoGeneration:GoogleFlow";

    /// <summary>Free credits refilled each UTC day.</summary>
    public int DailyCredits { get; set; } = 50;

    /// <summary>Credits for one AI video clip (Veo 3.1 fast/lite).</summary>
    public int CreditsPerVideoClip { get; set; } = 20;

    /// <summary>Credits for one AI still image (a small fraction of a clip - Imagen-class).</summary>
    public int CreditsPerImage { get; set; } = 3;

    /// <summary>
    /// Rough USD per credit ONCE the free daily pool is exhausted and calls
    /// fall through to the paid Gemini Veo API. Only used to put a number on
    /// the overage - within the pool the cost shown is $0.
    /// </summary>
    public decimal UsdPerOverageCredit { get; set; } = 0.10m;
}
