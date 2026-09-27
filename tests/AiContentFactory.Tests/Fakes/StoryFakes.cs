using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Tests.Fakes;

/// <summary>
/// In-memory IStoryRepository for service-level tests. Unlike the real EF
/// repository, this does NOT reproduce EF's Include-based population of
/// Story.Episodes (a private-field-backed navigation with no public
/// "attach" API on the domain type) - it just tracks each entity kind in
/// its own dictionary, which is enough to test StoryService's own logic
/// (existence checks, episode-number uniqueness, previous-episode
/// validation, lazy state creation). Story.Characters/Locations CAN be
/// reproduced for tests that need it, via Story.AttachCharacter/
/// AttachLocation (added for StoryContinuityManagerTests' Bible-generation
/// coverage) - callers just need to also add the character/location to
/// this repository's own Characters/Locations dictionary as the real
/// AddCharacterAsync/AddLocationAsync would.
/// </summary>
public sealed class FakeStoryRepository : IStoryRepository
{
    public Dictionary<Guid, Story> Stories { get; } = new();
    public Dictionary<Guid, StoryEpisode> Episodes { get; } = new();
    public Dictionary<Guid, StoryCharacter> Characters { get; } = new();
    public Dictionary<Guid, StoryLocation> Locations { get; } = new();
    public Dictionary<Guid, StoryState> States { get; } = new();

    public Task<Story?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Stories.GetValueOrDefault(id));

    public Task<IReadOnlyList<Story>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Story>>(Stories.Values.ToList());

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Stories.ContainsKey(id));

    public Task AddAsync(Story story, CancellationToken cancellationToken = default)
    {
        Stories[story.Id] = story;
        return Task.CompletedTask;
    }

    public Task<StoryEpisode?> GetEpisodeByIdAsync(Guid episodeId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Episodes.GetValueOrDefault(episodeId));

    public Task<StoryEpisode?> GetEpisodeByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Episodes.Values.FirstOrDefault(e => e.ContentProjectId == contentProjectId));

    public Task<IReadOnlyList<StoryEpisode>> GetEpisodesByStoryIdAsync(Guid storyId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoryEpisode>>(
            Episodes.Values.Where(e => e.StoryId == storyId).OrderBy(e => e.EpisodeNumber).ToList());

    public Task AddEpisodeAsync(StoryEpisode episode, CancellationToken cancellationToken = default)
    {
        Episodes[episode.Id] = episode;
        return Task.CompletedTask;
    }

    public Task AddCharacterAsync(StoryCharacter character, CancellationToken cancellationToken = default)
    {
        Characters[character.Id] = character;
        return Task.CompletedTask;
    }

    public Task AddLocationAsync(StoryLocation location, CancellationToken cancellationToken = default)
    {
        Locations[location.Id] = location;
        return Task.CompletedTask;
    }

    public Task<StoryState?> GetStateByStoryIdAsync(Guid storyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(States.Values.FirstOrDefault(s => s.StoryId == storyId));

    public Task AddStateAsync(StoryState state, CancellationToken cancellationToken = default)
    {
        States[state.Id] = state;
        return Task.CompletedTask;
    }

    // ---- Optional simulated "database" (see SimulateDatabaseRow) ----
    // Off by default: the tracked entity IS the row, exactly as before. When a test wants to model another
    // request changing the persisted row while this one holds a stale tracked entity, it registers a separate
    // "database row" here: ReloadCharacterAsync copies that row over the tracked instance (like EF's ReloadAsync)
    // and SaveChangesAsync copies the tracked instance back to it (like a committed write).

    private readonly Dictionary<Guid, StoryCharacter> _databaseRows = new();

    /// <summary>Rows of <see cref="FakeAssetReferenceRepository"/> whose ImagePath also count as "referenced" (episode copies share Story image keys).</summary>
    public FakeAssetReferenceRepository? AssetReferences { get; set; }

    /// <summary>Registers an independent copy of the tracked character as its persisted row and returns it, so a test can mutate the row as "another request".</summary>
    public StoryCharacter SimulateDatabaseRow(StoryCharacter tracked)
    {
        var row = (StoryCharacter)Activator.CreateInstance(typeof(StoryCharacter), nonPublic: true)!;
        CopyState(tracked, row);
        _databaseRows[tracked.Id] = row;
        return row;
    }

    public Task ReloadCharacterAsync(StoryCharacter character, CancellationToken cancellationToken = default)
    {
        if (_databaseRows.TryGetValue(character.Id, out var row))
        {
            CopyState(row, character);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsStoredImagePathReferencedAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        // Persisted state = the simulated rows where they exist, otherwise the (then authoritative) tracked entities.
        var characters = Characters.Values
            .Concat(Stories.Values.SelectMany(s => s.Characters))
            .GroupBy(c => c.Id)
            .Select(g => _databaseRows.TryGetValue(g.Key, out var row) ? row : g.First());

        var referenced =
            characters.Any(c => c.ReferenceImagePath == storedPath || c.PendingReferenceImagePath == storedPath) ||
            Locations.Values.Any(l => l.ReferenceImagePath == storedPath) ||
            (AssetReferences?.Rows.Any(r => r.ImagePath == storedPath) ?? false);
        return Task.FromResult(referenced);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var id in _databaseRows.Keys.ToList())
        {
            var tracked = Stories.Values.SelectMany(s => s.Characters).FirstOrDefault(c => c.Id == id) ?? Characters.GetValueOrDefault(id);
            if (tracked is not null)
            {
                CopyState(tracked, _databaseRows[id]);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Copies every settable (including private/protected) property, mirroring a full-row reload/write.</summary>
    private static void CopyState(StoryCharacter from, StoryCharacter to)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        for (var type = typeof(StoryCharacter); type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetProperties(flags | System.Reflection.BindingFlags.DeclaredOnly))
            {
                var setter = property.GetSetMethod(nonPublic: true);
                if (setter is null || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                setter.Invoke(to, new[] { property.GetValue(from) });
            }
        }
    }
}

/// <summary>
/// Controllable <see cref="IStoryVisualContextResolver"/> for service-level
/// tests - defaults to "not a Story-linked project" (both members null), the
/// same default every non-Story ContentProject gets from the real resolver.
/// </summary>
public sealed class FakeStoryVisualContextResolver : IStoryVisualContextResolver
{
    public string? VisualContext { get; set; }
    public IReadOnlyList<string>? CastAndLocationNames { get; set; }
    public IReadOnlyDictionary<string, string>? CastAndLocationVisualDescriptions { get; set; }
    public IReadOnlyDictionary<string, CharacterBehaviorProfile>? CharacterBehaviorProfiles { get; set; }

    public Task<string?> ResolveAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(VisualContext);

    public Task<IReadOnlyList<string>?> GetStoryCastAndLocationNamesAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CastAndLocationNames);

    public Task<IReadOnlyDictionary<string, string>?> GetCastAndLocationVisualDescriptionsAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CastAndLocationVisualDescriptions);

    public Task<IReadOnlyDictionary<string, CharacterBehaviorProfile>?> GetCharacterBehaviorProfilesAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CharacterBehaviorProfiles);
}
