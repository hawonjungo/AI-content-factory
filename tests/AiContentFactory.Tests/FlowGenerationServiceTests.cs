using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class FlowGenerationServiceTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static (FlowGenerationService Service, FakeVideoGenerationProvider Provider, CreditLedgerService Ledger, InMemoryGenerationAttemptRepository Repo)
        Build(CreditCostOptions? costs = null)
    {
        var repo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(repo, Options.Create(costs ?? new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        var provider = new FakeVideoGenerationProvider();
        var service = new FlowGenerationService(
            provider,
            ledger,
            Options.Create(costs ?? new CreditCostOptions()),
            Options.Create(new FlowModelOptions()),
            NullLogger<FlowGenerationService>.Instance);
        return (service, provider, ledger, repo);
    }

    private FlowClipRequest Request(Guid scene, VideoModelTier tier) =>
        new(_project, scene, "a shot", null, 8, tier);

    [Fact]
    public async Task Fast_tier_reserves_20_credits_and_selects_the_fast_model()
    {
        var (service, provider, ledger, _) = Build();

        var result = await service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Fast));

        Assert.Equal(GenerationAttemptStatus.Generating, result.Status);
        Assert.NotNull(result.VideoBytes);
        Assert.Equal(20, result.CreditsReserved);
        Assert.Equal(new FlowModelOptions().FastModel, provider.LastRequest!.Model);
        Assert.Equal(30, await ledger.GetRemainingDailyCreditsAsync()); // 50 - 20 reserved
    }

    [Fact]
    public async Task Lite_tier_reserves_10_credits_and_selects_the_lite_model()
    {
        var (service, provider, _, _) = Build();

        var result = await service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Lite));

        Assert.Equal(10, result.CreditsReserved);
        Assert.Equal(new FlowModelOptions().LiteModel, provider.LastRequest!.Model);
    }

    [Fact]
    public async Task A_provider_failure_records_the_attempt_as_failed_and_throws()
    {
        var (service, provider, ledger, repo) = Build();
        provider.Throw = new HttpRequestException("veo 503");

        var scene = Guid.NewGuid();
        await Assert.ThrowsAsync<FlowGenerationException>(() => service.GenerateClipAsync(Request(scene, VideoModelTier.Fast)));

        var row = Assert.Single(repo.Rows);
        Assert.Equal(GenerationAttemptStatus.Failed, row.Status);
        Assert.Contains("veo 503", row.FailureReason);
        // Failed attempts don't hold budget.
        Assert.Equal(50, await ledger.GetRemainingDailyCreditsAsync());
    }

    [Fact]
    public async Task Repeated_failures_for_one_scene_stop_at_the_retry_cap()
    {
        var (service, provider, _, _) = Build(new CreditCostOptions { MaxAttemptsPerScene = 2 });
        provider.Throw = new HttpRequestException("boom");
        var scene = Guid.NewGuid();

        await Assert.ThrowsAsync<FlowGenerationException>(() => service.GenerateClipAsync(Request(scene, VideoModelTier.Lite)));
        await Assert.ThrowsAsync<FlowGenerationException>(() => service.GenerateClipAsync(Request(scene, VideoModelTier.Lite)));

        // Third try is refused by the ledger before the provider is touched again.
        await Assert.ThrowsAsync<RetryLimitExceededException>(() => service.GenerateClipAsync(Request(scene, VideoModelTier.Lite)));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task An_already_generated_scene_is_not_regenerated()
    {
        var (service, provider, ledger, _) = Build();
        var scene = Guid.NewGuid();

        var first = await service.GenerateClipAsync(Request(scene, VideoModelTier.Fast));
        await ledger.CommitAsync(first.AttemptId, 20, Guid.NewGuid());

        var second = await service.GenerateClipAsync(Request(scene, VideoModelTier.Fast));

        Assert.True(second.AlreadyGenerated);
        Assert.Null(second.VideoBytes);
        Assert.Equal(1, provider.Calls); // provider was only hit once
    }

    [Fact]
    public async Task The_budget_stops_a_run_from_exceeding_the_daily_pool()
    {
        var (service, _, _, _) = Build();

        // 1 Fast (20) + 3 Lite (10) = 50, then the next clip must be refused.
        await service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Fast));
        await service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Lite));
        await service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Lite));
        await service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Lite));

        await Assert.ThrowsAsync<CreditBudgetExceededException>(
            () => service.GenerateClipAsync(Request(Guid.NewGuid(), VideoModelTier.Lite)));
    }
}
