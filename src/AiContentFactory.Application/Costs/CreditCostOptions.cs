using AiContentFactory.Domain.Generation;

namespace AiContentFactory.Application.Costs;

/// <summary>
/// The credit economy for AI generation, bound from the "Credits" config
/// section. Nothing in the pipeline hardcodes a credit number - every cost is
/// read from here so a provider price change is a config edit, not a code
/// change.
///
/// Defaults encode the reference daily plan:
///   50 credits/day = 1 x Fast (20) + 3 x Lite (10) = 50.
/// The allocation is NOT fixed at that shape though - the storyboard decides
/// how many clips of each tier to buy (see VideoAllocationPlanner), this only
/// says what each one costs.
/// </summary>
public class CreditCostOptions
{
    public const string SectionName = "Credits";

    /// <summary>Credits refreshed each UTC day, shared across every project on the account.</summary>
    public int DailyBudgetCredits { get; set; } = 50;

    /// <summary>Credits for one Fast-tier (hero / hook) AI video clip.</summary>
    public int FastVideoCredits { get; set; } = 20;

    /// <summary>Credits for one Lite-tier (story-beat) AI video clip.</summary>
    public int LiteVideoCredits { get; set; } = 10;

    /// <summary>Credits for one AI still image. Kept configurable; defaults to free.</summary>
    public int ImageCredits { get; set; } = 0;

    /// <summary>Credits for one narration TTS clip. Kept configurable; defaults to free.</summary>
    public int AudioCredits { get; set; } = 0;

    /// <summary>
    /// How many attempts (first try + retries) a single scene+kind may make
    /// before the ledger refuses further tries. Stops a persistently failing
    /// generation from draining the daily budget one retry at a time.
    /// </summary>
    public int MaxAttemptsPerScene { get; set; } = 2;

    public int VideoCreditsFor(VideoModelTier tier) => tier switch
    {
        VideoModelTier.Fast => FastVideoCredits,
        VideoModelTier.Lite => LiteVideoCredits,
        _ => LiteVideoCredits
    };

    public int CreditsFor(GenerationKind kind, VideoModelTier? tier = null) => kind switch
    {
        GenerationKind.Video => VideoCreditsFor(tier ?? VideoModelTier.Lite),
        GenerationKind.Image => ImageCredits,
        GenerationKind.Audio => AudioCredits,
        _ => 0
    };
}
