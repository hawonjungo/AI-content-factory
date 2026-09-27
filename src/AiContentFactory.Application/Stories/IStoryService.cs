namespace AiContentFactory.Application.Stories;

public interface IStoryService
{
    Task<StoryResponse> CreateAsync(CreateStoryRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoryResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<StoryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<StoryResponse?> UpdateAsync(Guid id, UpdateStoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns null if the Story doesn't exist. Throws
    /// <see cref="AiContentFactory.Domain.Exceptions.DomainException"/> if
    /// EpisodeNumber is already used within this Story, or if
    /// PreviousEpisodeId doesn't reference an episode of the same Story.
    /// </summary>
    Task<StoryEpisodeResponse?> CreateEpisodeAsync(Guid storyId, CreateStoryEpisodeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null if the Story doesn't exist. Ordered by EpisodeNumber.</summary>
    Task<IReadOnlyList<StoryEpisodeSummaryResponse>?> GetEpisodesAsync(Guid storyId, CancellationToken cancellationToken = default);

    /// <summary>Null if the Story doesn't exist, or the episode doesn't exist, or belongs to a different Story.</summary>
    Task<StoryEpisodeResponse?> GetEpisodeByIdAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plain CRUD script edit - no AI call, unlike
    /// <see cref="AiContentFactory.Application.Stories.Continuity.IStoryContinuityManager.WriteScriptAsync"/>.
    /// Lets a human submit a manually-edited script directly via
    /// <see cref="AiContentFactory.Domain.Stories.StoryEpisode.SetScript"/>.
    /// Null if the Story doesn't exist, or the episode doesn't exist, or
    /// belongs to a different Story. Throws
    /// <see cref="AiContentFactory.Domain.Exceptions.DomainException"/> if
    /// Script is blank.
    /// </summary>
    Task<StoryEpisodeResponse?> SetEpisodeScriptAsync(Guid storyId, Guid episodeId, UpdateStoryEpisodeScriptRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null if the Story doesn't exist.</summary>
    Task<StoryCharacterResponse?> CreateCharacterAsync(Guid storyId, CreateStoryCharacterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plain CRUD edit - no AI call. Null if the Story doesn't exist, the
    /// character doesn't exist, or the character belongs to a different
    /// Story. Throws <see cref="AiContentFactory.Domain.Exceptions.DomainException"/>
    /// if Name is blank.
    /// </summary>
    Task<StoryCharacterResponse?> UpdateCharacterAsync(Guid storyId, Guid characterId, UpdateStoryCharacterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Null if the Story doesn't exist.</summary>
    Task<StoryLocationResponse?> CreateLocationAsync(Guid storyId, CreateStoryLocationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Null if the Story doesn't exist. Otherwise lazily creates+persists an
    /// empty <see cref="AiContentFactory.Domain.Stories.StoryState"/> the
    /// first time it's requested, since <c>Story.Create</c> doesn't create
    /// one automatically.
    /// </summary>
    Task<StoryStateResponse?> GetStateAsync(Guid storyId, CancellationToken cancellationToken = default);
}
