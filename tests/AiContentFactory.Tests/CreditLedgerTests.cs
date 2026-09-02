using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class CreditLedgerTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static (CreditLedgerService Ledger, InMemoryGenerationAttemptRepository Repo) Build(CreditCostOptions? costs = null)
    {
        var repo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(
            repo,
            Options.Create(costs ?? new CreditCostOptions()),
            NullLogger<CreditLedgerService>.Instance);
        return (ledger, repo);
    }

    private CreditReservationRequest Video(Guid? sceneId, VideoModelTier tier) =>
        new(_project, sceneId, GenerationKind.Video, "veo", tier == VideoModelTier.Fast ? "veo-fast" : "veo-lite", tier);

    [Fact]
    public async Task Default_daily_budget_is_50_credits()
    {
        var (ledger, _) = Build();

        Assert.Equal(50, new CreditCostOptions().DailyBudgetCredits);
        Assert.Equal(50, await ledger.GetRemainingDailyCreditsAsync());
    }

    [Fact]
    public async Task Reference_strategy_20_10_10_10_exactly_fills_the_daily_budget()
    {
        var (ledger, _) = Build();

        await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Fast)); // 20
        await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite)); // 10
        await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite)); // 10
        await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite)); // 10  => 50 total

        Assert.Equal(0, await ledger.GetRemainingDailyCreditsAsync());

        // The next clip of any tier must be refused - the pool is empty.
        await Assert.ThrowsAsync<CreditBudgetExceededException>(
            () => ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite)));
    }

    [Fact]
    public async Task Reservation_is_refused_when_credits_are_insufficient()
    {
        var (ledger, _) = Build(new CreditCostOptions { DailyBudgetCredits = 15 });

        var ex = await Assert.ThrowsAsync<CreditBudgetExceededException>(
            () => ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Fast))); // needs 20

        Assert.Equal(20, ex.RequiredCredits);
        Assert.Equal(15, ex.RemainingCredits);
    }

    [Fact]
    public async Task Still_reserved_credits_count_against_the_budget()
    {
        var (ledger, _) = Build();

        await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Fast)); // reserved, not committed

        Assert.Equal(30, await ledger.GetRemainingDailyCreditsAsync());
    }

    [Fact]
    public async Task Failed_generation_is_recorded_and_releases_its_reservation()
    {
        var (ledger, repo) = Build();

        var attempt = await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Fast));
        Assert.Equal(30, await ledger.GetRemainingDailyCreditsAsync());

        await ledger.FailAsync(attempt.Id, "provider 500");

        var row = Assert.Single(repo.Rows);
        Assert.Equal(GenerationAttemptStatus.Failed, row.Status);
        Assert.Equal("provider 500", row.FailureReason);
        Assert.Null(row.ActualCredits);
        // A failed attempt never counts as spend.
        Assert.Equal(50, await ledger.GetRemainingDailyCreditsAsync());
    }

    [Fact]
    public async Task Committed_generation_counts_actual_credits()
    {
        var (ledger, repo) = Build();

        var attempt = await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite));
        await ledger.CommitAsync(attempt.Id, actualCredits: 10, assetId: Guid.NewGuid());

        Assert.Equal(GenerationAttemptStatus.Completed, repo.Rows.Single().Status);
        Assert.Equal(40, await ledger.GetRemainingDailyCreditsAsync());
    }

    [Fact]
    public async Task Retry_limit_stops_a_persistently_failing_scene_from_draining_credits()
    {
        var (ledger, _) = Build(new CreditCostOptions { MaxAttemptsPerScene = 2 });
        var scene = Guid.NewGuid();

        var a1 = await ledger.ReserveAsync(Video(scene, VideoModelTier.Lite));
        await ledger.FailAsync(a1.Id, "fail 1");

        var a2 = await ledger.ReserveAsync(Video(scene, VideoModelTier.Lite));
        Assert.Equal(GenerationAttemptStatus.Retrying, a2.Status);
        await ledger.FailAsync(a2.Id, "fail 2");

        await Assert.ThrowsAsync<RetryLimitExceededException>(
            () => ledger.ReserveAsync(Video(scene, VideoModelTier.Lite)));
    }

    [Fact]
    public async Task Reservation_is_idempotent_once_a_scene_kind_has_succeeded()
    {
        var (ledger, repo) = Build();
        var scene = Guid.NewGuid();

        var first = await ledger.ReserveAsync(Video(scene, VideoModelTier.Lite));
        await ledger.CommitAsync(first.Id, actualCredits: 10, assetId: Guid.NewGuid());

        var second = await ledger.ReserveAsync(Video(scene, VideoModelTier.Lite));

        Assert.Equal(first.Id, second.Id);          // same row, nothing new reserved
        Assert.Single(repo.Rows);
        Assert.Equal(40, await ledger.GetRemainingDailyCreditsAsync());
    }

    [Fact]
    public async Task Validated_is_reachable_only_from_completed()
    {
        var (ledger, repo) = Build();

        var attempt = await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite));
        await ledger.CommitAsync(attempt.Id, 10, Guid.NewGuid());
        await ledger.MarkValidatedAsync(attempt.Id);

        Assert.Equal(GenerationAttemptStatus.Validated, repo.Rows.Single().Status);
    }
}
