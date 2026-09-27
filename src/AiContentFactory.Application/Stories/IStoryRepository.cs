using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

public interface IStoryRepository
{
    /// <summary>Loads a Story with its Characters/Locations/Episodes/State navigations.</summary>
    Task<Story?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Same shape as <see cref="GetByIdAsync"/>, for the list endpoint.</summary>
    Task<IReadOnlyList<Story>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Lightweight existence check, used before adding a child (character/location/episode/state) without loading the whole aggregate.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Story story, CancellationToken cancellationToken = default);

    /// <summary>Unscoped lookup - callers must verify <see cref="StoryEpisode.StoryId"/> themselves when a specific story is expected.</summary>
    Task<StoryEpisode?> GetEpisodeByIdAsync(Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverse lookup: finds the episode (if any) linked to a given ContentProject
    /// via <see cref="StoryEpisode.ContentProjectId"/>. Null when the ContentProject
    /// isn't Story-linked (the normal/default case for every non-Story project).
    /// A plain filter, not a relational join - see <see cref="StoryEpisode.ContentProjectId"/>'s
    /// own remarks on why there is no FK.
    /// </summary>
    Task<StoryEpisode?> GetEpisodeByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoryEpisode>> GetEpisodesByStoryIdAsync(Guid storyId, CancellationToken cancellationToken = default);

    Task AddEpisodeAsync(StoryEpisode episode, CancellationToken cancellationToken = default);

    Task AddCharacterAsync(StoryCharacter character, CancellationToken cancellationToken = default);

    Task AddLocationAsync(StoryLocation location, CancellationToken cancellationToken = default);

    Task<StoryState?> GetStateByStoryIdAsync(Guid storyId, CancellationToken cancellationToken = default);

    Task AddStateAsync(StoryState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads the character's CURRENT persisted values into the tracked instance. A tracked
    /// entity is not refreshed by a second query, so code that held a character across a slow
    /// call (an image-provider request) must reload it before deciding on its state.
    /// </summary>
    Task ReloadCharacterAsync(StoryCharacter character, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when any persisted row still points at this storage key: a Story character's
    /// current/pending reference image, a Story location's image, or a ContentProject
    /// AssetReference (episode projects copy the Story image by sharing its key). Callers
    /// must never delete a stored file for which this is true.
    /// </summary>
    Task<bool> IsStoredImagePathReferencedAsync(string storedPath, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
