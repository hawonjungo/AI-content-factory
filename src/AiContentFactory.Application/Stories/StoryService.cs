using AiContentFactory.Application.Presets;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

public class StoryService : IStoryService
{
    private readonly IStoryRepository _repository;

    public StoryService(IStoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<StoryResponse> CreateAsync(CreateStoryRequest request, CancellationToken cancellationToken = default)
    {
        var story = Story.Create(request.Title, request.Premise, request.Niche, request.Language, request.AspectRatio);
        if (!string.IsNullOrWhiteSpace(request.StylePresetId))
        {
            story.SetStylePreset(EnsureKnownStyle(request.StylePresetId));
        }

        await _repository.AddAsync(story, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryResponse.FromDomain(story);
    }

    public async Task<IReadOnlyList<StoryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var stories = await _repository.GetAllAsync(cancellationToken);
        return stories.Select(StoryResponse.FromDomain).ToList();
    }

    public async Task<StoryResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(id, cancellationToken);
        return story is null ? null : StoryResponse.FromDomain(story);
    }

    public async Task<StoryResponse?> UpdateAsync(Guid id, UpdateStoryRequest request, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(id, cancellationToken);
        if (story is null)
        {
            return null;
        }

        // Validate before mutating anything so a bad style id leaves the story untouched.
        var stylePresetId = request.StylePresetId is null ? null : EnsureKnownStyle(request.StylePresetId);

        story.UpdateDetails(request.Title, request.Premise, request.Niche);
        if (request.StylePresetId is not null)
        {
            // Non-null: an id sets it, blank clears it. Null/omitted leaves it as is.
            story.SetStylePreset(stylePresetId);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return StoryResponse.FromDomain(story);
    }

    /// <summary>
    /// Same rule and message shape as <see cref="PresetService"/> applying a
    /// style to a ContentProject ("Unknown style preset '{id}'."). A blank id
    /// is not an error (it means "no style") and is returned as null. Returns
    /// the catalog's canonical id.
    /// </summary>
    private static string? EnsureKnownStyle(string stylePresetId)
    {
        var trimmed = stylePresetId.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // The catalog lookup is case-insensitive; store the canonical id so
        // "PIXAR-3D" and "pixar-3d" can never diverge downstream.
        var style = PresetCatalog.FindStyle(trimmed)
            ?? throw new DomainException($"Unknown style preset '{trimmed}'.");

        return style.Id;
    }

    public async Task<StoryEpisodeResponse?> CreateEpisodeAsync(Guid storyId, CreateStoryEpisodeRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _repository.ExistsAsync(storyId, cancellationToken))
        {
            return null;
        }

        var existingEpisodes = await _repository.GetEpisodesByStoryIdAsync(storyId, cancellationToken);
        if (existingEpisodes.Any(e => e.EpisodeNumber == request.EpisodeNumber))
        {
            throw new DomainException($"Episode number {request.EpisodeNumber} already exists for this Story.");
        }

        if (request.PreviousEpisodeId is { } previousEpisodeId)
        {
            var previousEpisode = await _repository.GetEpisodeByIdAsync(previousEpisodeId, cancellationToken);
            if (previousEpisode is null || previousEpisode.StoryId != storyId)
            {
                throw new DomainException("PreviousEpisodeId must reference an existing episode of the same Story.");
            }
        }

        var episode = StoryEpisode.Create(storyId, request.EpisodeNumber, request.Title, request.PreviousEpisodeId);

        await _repository.AddEpisodeAsync(episode, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryEpisodeResponse.FromDomain(episode);
    }

    public async Task<IReadOnlyList<StoryEpisodeSummaryResponse>?> GetEpisodesAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        if (!await _repository.ExistsAsync(storyId, cancellationToken))
        {
            return null;
        }

        var episodes = await _repository.GetEpisodesByStoryIdAsync(storyId, cancellationToken);
        return episodes
            .OrderBy(e => e.EpisodeNumber)
            .Select(StoryEpisodeSummaryResponse.FromDomain)
            .ToList();
    }

    public async Task<StoryEpisodeResponse?> GetEpisodeByIdAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId)
        {
            return null;
        }

        return StoryEpisodeResponse.FromDomain(episode);
    }

    public async Task<StoryEpisodeResponse?> SetEpisodeScriptAsync(Guid storyId, Guid episodeId, UpdateStoryEpisodeScriptRequest request, CancellationToken cancellationToken = default)
    {
        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId)
        {
            return null;
        }

        episode.SetScript(request.Script);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryEpisodeResponse.FromDomain(episode);
    }

    public async Task<StoryCharacterResponse?> CreateCharacterAsync(Guid storyId, CreateStoryCharacterRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _repository.ExistsAsync(storyId, cancellationToken))
        {
            return null;
        }

        var character = StoryCharacter.Create(
            storyId, request.Name, request.Description, request.VisualDescription, request.BehaviorProfile,
            request.Kind ?? CharacterKind.Unspecified, request.Species, request.ClothingAndAccessories, request.DistinctiveFeatures);

        await _repository.AddCharacterAsync(character, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryCharacterResponse.FromDomain(character);
    }

    public async Task<StoryCharacterResponse?> UpdateCharacterAsync(Guid storyId, Guid characterId, UpdateStoryCharacterRequest request, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (story is null || character is null)
        {
            return null;
        }

        character.Update(request.Name, request.Description, request.VisualDescription, request.BehaviorProfile);
        // Canonical identity fields: null/omitted = unchanged, blank = clear. Kept separate from
        // Update so it can never reset BehaviorProfile. A length-limit DomainException surfaces here
        // BEFORE SaveChanges, so nothing from this request is persisted.
        character.UpdateCanonicalProfile(request.Kind, request.Species, request.ClothingAndAccessories, request.DistinctiveFeatures);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryCharacterResponse.FromDomain(character);
    }

    public async Task<StoryLocationResponse?> CreateLocationAsync(Guid storyId, CreateStoryLocationRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _repository.ExistsAsync(storyId, cancellationToken))
        {
            return null;
        }

        var location = StoryLocation.Create(storyId, request.Name, request.Description, request.VisualDescription);

        await _repository.AddLocationAsync(location, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryLocationResponse.FromDomain(location);
    }

    public async Task<StoryStateResponse?> GetStateAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        if (!await _repository.ExistsAsync(storyId, cancellationToken))
        {
            return null;
        }

        var state = await _repository.GetStateByStoryIdAsync(storyId, cancellationToken);
        if (state is null)
        {
            state = StoryState.Create(storyId);
            await _repository.AddStateAsync(state, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return StoryStateResponse.FromDomain(state);
    }
}
