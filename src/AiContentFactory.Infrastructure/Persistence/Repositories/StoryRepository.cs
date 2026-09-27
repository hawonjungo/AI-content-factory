using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.Stories;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class StoryRepository : IStoryRepository
{
    private readonly AppDbContext _db;

    public StoryRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Story?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Stories
            .Include(s => s.Characters)
            .Include(s => s.Locations)
            .Include(s => s.Episodes)
            .Include(s => s.State)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Story>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.Stories
            .Include(s => s.Characters)
            .Include(s => s.Locations)
            .Include(s => s.Episodes)
            .Include(s => s.State)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Stories.AnyAsync(s => s.Id == id, cancellationToken);

    public async Task AddAsync(Story story, CancellationToken cancellationToken = default) =>
        await _db.Stories.AddAsync(story, cancellationToken);

    public Task<StoryEpisode?> GetEpisodeByIdAsync(Guid episodeId, CancellationToken cancellationToken = default) =>
        _db.StoryEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId, cancellationToken);

    public Task<StoryEpisode?> GetEpisodeByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        _db.StoryEpisodes.FirstOrDefaultAsync(e => e.ContentProjectId == contentProjectId, cancellationToken);

    public async Task<IReadOnlyList<StoryEpisode>> GetEpisodesByStoryIdAsync(Guid storyId, CancellationToken cancellationToken = default) =>
        await _db.StoryEpisodes
            .Where(e => e.StoryId == storyId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync(cancellationToken);

    public async Task AddEpisodeAsync(StoryEpisode episode, CancellationToken cancellationToken = default) =>
        await _db.StoryEpisodes.AddAsync(episode, cancellationToken);

    public async Task AddCharacterAsync(StoryCharacter character, CancellationToken cancellationToken = default) =>
        await _db.StoryCharacters.AddAsync(character, cancellationToken);

    public async Task AddLocationAsync(StoryLocation location, CancellationToken cancellationToken = default) =>
        await _db.StoryLocations.AddAsync(location, cancellationToken);

    public Task<StoryState?> GetStateByStoryIdAsync(Guid storyId, CancellationToken cancellationToken = default) =>
        _db.StoryStates.FirstOrDefaultAsync(s => s.StoryId == storyId, cancellationToken);

    public async Task AddStateAsync(StoryState state, CancellationToken cancellationToken = default) =>
        await _db.StoryStates.AddAsync(state, cancellationToken);

    public Task ReloadCharacterAsync(StoryCharacter character, CancellationToken cancellationToken = default) =>
        _db.Entry(character).ReloadAsync(cancellationToken);

    public async Task<bool> IsStoredImagePathReferencedAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        // Straight database queries (never the tracked in-memory values), so a concurrent request's
        // committed write is seen. Episode ContentProjects share the Story image's storage key.
        return await _db.StoryCharacters.AnyAsync(c => c.ReferenceImagePath == storedPath || c.PendingReferenceImagePath == storedPath, cancellationToken)
            || await _db.StoryLocations.AnyAsync(l => l.ReferenceImagePath == storedPath, cancellationToken)
            || await _db.AssetReferences.AnyAsync(r => r.ImagePath == storedPath, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
