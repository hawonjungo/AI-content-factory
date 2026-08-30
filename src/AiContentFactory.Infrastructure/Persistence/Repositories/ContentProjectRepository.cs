using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class ContentProjectRepository : IContentProjectRepository
{
    private readonly AppDbContext _db;

    public ContentProjectRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.ContentProjects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.ContentProjects
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ContentProject project, CancellationToken cancellationToken = default) =>
        await _db.ContentProjects.AddAsync(project, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
