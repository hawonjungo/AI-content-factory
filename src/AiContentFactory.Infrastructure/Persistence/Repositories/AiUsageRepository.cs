using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.Costs;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence.Repositories;

public class AiUsageRepository : IAiUsageRepository
{
    private readonly AppDbContext _db;

    public AiUsageRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AiUsageRecord record, CancellationToken cancellationToken = default) =>
        await _db.AiUsageRecords.AddAsync(record, cancellationToken);

    public async Task<decimal> GetSpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var sum = await _db.AiUsageRecords
            .Where(r => r.CreatedAt >= since)
            .SumAsync(r => (decimal?)r.EstimatedCostUsd, cancellationToken);
        return sum ?? 0m;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
