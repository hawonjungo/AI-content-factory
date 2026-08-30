using AiContentFactory.Domain.Scripts;

namespace AiContentFactory.Application.Scripts;

public interface IScriptService
{
    Task<ScriptResponse?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<ScriptResponse> UpsertAsync(Guid contentProjectId, UpsertScriptRequest request, CancellationToken cancellationToken = default);
}

public class ScriptService : IScriptService
{
    private readonly IScriptRepository _repository;

    public ScriptService(IScriptRepository repository)
    {
        _repository = repository;
    }

    public async Task<ScriptResponse?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var script = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        return script is null ? null : ScriptResponse.FromDomain(script);
    }

    public async Task<ScriptResponse> UpsertAsync(Guid contentProjectId, UpsertScriptRequest request, CancellationToken cancellationToken = default)
    {
        var script = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        if (script is null)
        {
            script = Script.Create(contentProjectId);
            await _repository.AddAsync(script, cancellationToken);
        }

        script.UpdateSections(request.Hook, request.Introduction, request.Body, request.Escalation, request.Payoff, request.CallToAction);
        await _repository.SaveChangesAsync(cancellationToken);

        return ScriptResponse.FromDomain(script);
    }
}
