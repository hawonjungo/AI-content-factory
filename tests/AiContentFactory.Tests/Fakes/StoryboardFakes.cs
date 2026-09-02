using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Tests.Fakes;

/// <summary>Holds one storyboard in memory; enough for ClipPlanService.</summary>
public sealed class FakeStoryboardRepository : IStoryboardRepository
{
    public Storyboard? Current { get; private set; }

    public FakeStoryboardRepository(Storyboard? seed = null) => Current = seed;

    public Task<Storyboard?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task<Storyboard?> GetByContentProjectIdAsyncNoTracking(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task AddAsync(Storyboard storyboard, CancellationToken cancellationToken = default)
    {
        Current = storyboard;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteScenesByStoryboardIdAsync(Guid storyboardId, CancellationToken cancellationToken = default)
    {
        if (Current is not null)
        {
            foreach (var scene in Current.Scenes.ToList())
            {
                Current.RemoveScene(scene.Id);
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearChangeTrackerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeScriptService : IScriptService
{
    private readonly ScriptResponse? _script;

    public FakeScriptService(ScriptResponse? script) => _script = script;

    public Task<ScriptResponse?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_script);

    public Task<ScriptResponse> UpsertAsync(Guid contentProjectId, UpsertScriptRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

public sealed class FakeContentProjectRepository : IContentProjectRepository
{
    private readonly ContentProject _project;

    public FakeContentProjectRepository(ContentProject project) => _project = project;

    public Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ContentProject?>(_project);

    public Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ContentProject>>(new[] { _project });

    public Task AddAsync(ContentProject project, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
