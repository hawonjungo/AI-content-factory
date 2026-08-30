using AiContentFactory.Application.Scripts;
using AiContentFactory.Domain.Scripts;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class ScriptRepository : IScriptRepository
{
    private readonly AppDbContext _db;

    public ScriptRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Script?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        _db.Scripts.FirstOrDefaultAsync(s => s.ContentProjectId == contentProjectId, cancellationToken);

    public async Task AddAsync(Script script, CancellationToken cancellationToken = default) =>
        await _db.Scripts.AddAsync(script, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
