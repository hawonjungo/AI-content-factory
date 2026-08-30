using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Storyboards;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class StoryboardRepository : IStoryboardRepository
{
    private readonly AppDbContext _db;

    public StoryboardRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Storyboard?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        _db.Storyboards
            .Include(s => s.Scenes)
            .FirstOrDefaultAsync(s => s.ContentProjectId == contentProjectId, cancellationToken);

    public async Task AddAsync(Storyboard storyboard, CancellationToken cancellationToken = default) =>
        await _db.Storyboards.AddAsync(storyboard, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
