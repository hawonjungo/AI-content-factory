using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Tests.Fakes;

/// <summary>Records which scenes were asked to generate; never touches a provider.</summary>
public sealed class SpySceneAssetGenerator : ISceneAssetGenerator
{
    public List<Guid> ClipCalls { get; } = new();
    public List<Guid> VoiceCalls { get; } = new();

    public Task<SceneGenerationContext> BuildContextAsync(ContentProject project, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SceneGenerationContext(project.Id, project.AspectRatio, null!, null!, null, null));

    public Task GenerateClipAsync(SceneGenerationContext context, SceneResponse scene, bool refreshPrompt, CancellationToken cancellationToken = default)
    {
        ClipCalls.Add(scene.Id);
        return Task.CompletedTask;
    }

    public Task GenerateVoiceAsync(SceneGenerationContext context, SceneResponse scene, CancellationToken cancellationToken = default)
    {
        VoiceCalls.Add(scene.Id);
        return Task.CompletedTask;
    }
}

/// <summary>Reference-only quota manager: reports plenty of credits, records nothing.</summary>
public sealed class FakeGoogleFlowQuotaManager : IGoogleFlowQuotaManager
{
    public int Remaining { get; set; } = 50;

    public Task<QuotaCheckResult> CheckDailyQuotaAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuotaCheckResult(true, Remaining, 0, 100, DateTime.UtcNow.Date.AddDays(1)));

    public Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Remaining);

    public Task RecordVideoGenerationAsync(Guid projectId, Guid? sceneId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordImageGenerationAsync(Guid projectId, Guid? sceneId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<QuotaSummary> GetTodaysSummaryAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QuotaSummary(50, 0, Remaining, 0, new List<GoogleFlowVideoGenerationRecord>()));

    public Task ResetDailyQuotaAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
