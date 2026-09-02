using AiContentFactory.Domain.Costs;

namespace AiContentFactory.Application.Costs;

public interface IAiUsageRepository
{
    Task AddAsync(AiUsageRecord record, CancellationToken cancellationToken = default);
    Task<decimal> GetSpendSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
