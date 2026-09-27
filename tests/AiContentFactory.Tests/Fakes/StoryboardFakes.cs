using AiContentFactory.Application.Agents;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Tests.Fakes;

/// <summary>Captures the last <see cref="PromptAgentInput"/> it was asked to turn into shot data - so tests can assert a caller wired StoryVisualContext (or didn't) without a real LLM call.</summary>
public sealed class FakePromptAgent : IPromptAgent
{
    public PromptAgentInput? LastInput { get; private set; }
    public int Calls { get; private set; }
    public Func<PromptAgentInput, PromptAgentOutput>? Respond { get; set; }

    public Task<PromptAgentOutput> GenerateAsync(PromptAgentInput input, CancellationToken cancellationToken = default)
    {
        LastInput = input;
        Calls++;
        return Task.FromResult(Respond?.Invoke(input) ?? new PromptAgentOutput("test action", CameraMovement.Static, null, null));
    }
}

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

    public bool DeleteWithProjectDataCalled { get; private set; }

    public Task DeleteWithProjectDataAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        DeleteWithProjectDataCalled = true;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
