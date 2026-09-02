using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class CreditLedgerUsageTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static (CreditLedgerService Ledger, InMemoryGenerationAttemptRepository Repo) Build()
    {
        var repo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(repo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        return (ledger, repo);
    }

    private CreditReservationRequest Video(Guid scene, VideoModelTier tier) =>
        new(_project, scene, GenerationKind.Video, "veo", tier == VideoModelTier.Fast ? "veo-fast" : "veo-lite", tier);

    [Fact]
    public async Task Empty_day_reports_the_full_budget_available()
    {
        var (ledger, _) = Build();

        var usage = await ledger.GetDailyUsageAsync();

        Assert.Equal(50, usage.DailyBudget);
        Assert.Equal(0, usage.Reserved);
        Assert.Equal(0, usage.Used);
        Assert.Equal(50, usage.Remaining);
        Assert.Equal(0, usage.FailedToday);
    }

    [Fact]
    public async Task Reserved_committed_and_failed_are_reported_separately()
    {
        var (ledger, _) = Build();

        // 1 Fast committed (20 used), 1 Lite still reserved (10), 1 Lite failed (0, but counted as failed).
        var fast = await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Fast));
        await ledger.CommitAsync(fast.Id, 20, Guid.NewGuid());

        await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite));

        var failed = await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite));
        await ledger.FailAsync(failed.Id, "boom");

        var usage = await ledger.GetDailyUsageAsync();

        Assert.Equal(20, usage.Used);
        Assert.Equal(10, usage.Reserved);
        Assert.Equal(20, usage.Remaining);       // 50 - 20 used - 10 reserved
        Assert.Equal(1, usage.FailedToday);      // failures are shown, not hidden, and cost nothing
    }

    [Fact]
    public async Task Validated_attempts_still_count_as_used()
    {
        var (ledger, _) = Build();

        var a = await ledger.ReserveAsync(Video(Guid.NewGuid(), VideoModelTier.Lite));
        await ledger.CommitAsync(a.Id, 10, Guid.NewGuid());
        await ledger.MarkValidatedAsync(a.Id);

        var usage = await ledger.GetDailyUsageAsync();

        Assert.Equal(10, usage.Used);
        Assert.Equal(40, usage.Remaining);
    }
}
