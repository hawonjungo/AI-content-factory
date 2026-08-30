using AiContentFactory.Domain.Scripts;

namespace AiContentFactory.Application.Scripts;

public interface IScriptRepository
{
    Task<Script?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task AddAsync(Script script, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
