using AiContentFactory.Application.Qa;
using AiContentFactory.Domain.Qa;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class QaScoreRepository : IQaScoreRepository
{
    private readonly AppDbContext _db;

    public QaScoreRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(QaScore score, CancellationToken cancellationToken = default) =>
        await _db.QaScores.AddAsync(score, cancellationToken);

    public async Task<IReadOnlyList<QaScore>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        await _db.QaScores
            .Where(s => s.ContentProjectId == contentProjectId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
