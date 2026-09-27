namespace AiContentFactory.Application.Stories.Continuity;

/// <summary>
/// Orchestrates the Story/Series AI generation workflow (Bible -> Episode
/// Outline -> Script -> Validate -> Finalize) on top of the four Continuity
/// agents and <see cref="IStoryRepository"/>. This is plain coordination
/// logic, NOT an LLM agent itself - it never calls an <c>ILlmProvider</c>
/// directly.
///
/// Every method returns null when the referenced Story/Episode does not
/// exist (callers map this to 404), and throws
/// <see cref="AiContentFactory.Domain.Exceptions.DomainException"/> for
/// business-rule failures (e.g. writing a script before an outline exists) -
/// same convention as <see cref="IStoryService"/>.
/// </summary>
public interface IStoryContinuityManager
{
    /// <summary>Generates and persists the Story's Bible. One LLM call.</summary>
    Task<StoryBibleResponse?> PlanStoryAsync(Guid storyId, PlanStoryBibleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fetches the persisted Bible without generating anything. Null if the Story or its Bible doesn't exist yet.</summary>
    Task<StoryBibleResponse?> GetBibleAsync(Guid storyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plain CRUD edit - no AI call. Replaces the Bible wholesale with the
    /// given fields (same replace-not-merge semantics as <see cref="Domain.Stories.Story.SetBible"/>).
    /// Null if the Story doesn't exist.
    /// </summary>
    Task<StoryBibleResponse?> UpdateBibleAsync(Guid storyId, UpdateStoryBibleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and persists an Episode Outline from the Bible, the current
    /// Story State, and the single previous episode's summary. One LLM call.
    /// Throws <see cref="AiContentFactory.Domain.Exceptions.DomainException"/>
    /// if the Story has no Bible yet.
    /// </summary>
    Task<StoryEpisodeOutlineResponse?> PlanEpisodeAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>Fetches the persisted Outline without generating anything. Null if the Story/Episode or its Outline doesn't exist yet.</summary>
    Task<StoryEpisodeOutlineResponse?> GetOutlineAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a production-ready script from the episode's own persisted
    /// Outline, persists it (joined into the single Script text field) plus a
    /// Summary. One LLM call. Throws
    /// <see cref="AiContentFactory.Domain.Exceptions.DomainException"/> if the
    /// episode has no Outline yet.
    /// </summary>
    Task<StoryEpisodeResponse?> WriteScriptAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the continuity validator against the episode's current script (or
    /// outline, if no script exists yet) WITHOUT mutating anything. One LLM
    /// call.
    /// </summary>
    Task<ContinuityValidationResponse?> ValidateEpisodeAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the episode first; if invalid, returns the validation result
    /// immediately WITHOUT touching StoryState or completing the episode (so
    /// the caller can regenerate). If valid, analyzes the finalized script,
    /// applies the result to StoryState, freezes a snapshot into the episode
    /// via <see cref="AiContentFactory.Domain.Stories.StoryEpisode.Complete"/>,
    /// and returns success. At most two LLM calls (validate + analysis), and
    /// only one when validation fails.
    /// </summary>
    Task<FinalizeEpisodeResponse?> FinalizeEpisodeAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);
}
