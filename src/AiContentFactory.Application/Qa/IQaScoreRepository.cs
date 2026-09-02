using AiContentFactory.Domain.Qa;

namespace AiContentFactory.Application.Qa;

public interface IQaScoreRepository
{
    Task AddAsync(QaScore score, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QaScore>> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
