namespace AiContentFactory.Domain.Generation;

/// <summary>What a <see cref="GenerationAttempt"/> was trying to produce.</summary>
public enum GenerationKind
{
    Image = 0,
    Video = 1,
    Audio = 2
}

/// <summary>
/// Lifecycle of one generation attempt. Mirrors the coarse job states the
/// wizard already understands (see GenerationProgress / SceneStatus) but adds
/// the two the pipeline was missing: <see cref="Retrying"/> (a fresh attempt
/// after a recorded failure, still inside the retry cap) and
/// <see cref="Validated"/> (post-generation checks passed).
/// </summary>
public enum GenerationAttemptStatus
{
    Pending = 0,
    Generating = 1,
    Completed = 2,
    Failed = 3,
    Retrying = 4,
    Validated = 5
}

/// <summary>
/// AI-video model tiers the storyboard allocates between. Names, actual model
/// ids and credit costs are all configuration - this enum is only the choice
/// "expensive hero clip" vs "cheaper story-beat clip".
/// </summary>
public enum VideoModelTier
{
    /// <summary>Hero / hook clip - highest quality, highest credit cost (default 20).</summary>
    Fast = 0,

    /// <summary>Story-beat clip - lighter model, lower credit cost (default 10).</summary>
    Lite = 1
}
