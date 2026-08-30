using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Storyboards;

public interface IStoryboardRepository
{
    Task<Storyboard?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task AddAsync(Storyboard storyboard, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
