using AiContentFactory.Domain.Costs;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Costs;

public class AiUsageTrackerService : IAiUsageTracker
{
    private readonly IAiUsageRepository _repository;
    private readonly BudgetOptions _budgetOptions;

    public AiUsageTrackerService(IAiUsageRepository repository, IOptions<BudgetOptions> budgetOptions)
    {
        _repository = repository;
        _budgetOptions = budgetOptions.Value;
    }

    public async Task RecordAsync(RecordUsageInput input, CancellationToken cancellationToken = default)
    {
        var record = AiUsageRecord.Create(input.Provider, input.Model, input.Operation, input.EstimatedCostUsd, input.ContentProjectId, input.SceneId);
        await _repository.AddAsync(record, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public Task<decimal> GetCurrentMonthSpendAsync(CancellationToken cancellationToken = default)
    {
        var startOfMonth = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return _repository.GetSpendSinceAsync(startOfMonth, cancellationToken);
    }

    public async Task EnsureBudgetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var currentSpend = await GetCurrentMonthSpendAsync(cancellationToken);
        if (currentSpend >= _budgetOptions.MonthlyLimitUsd)
        {
            throw new BudgetExceededException(currentSpend, _budgetOptions.MonthlyLimitUsd);
        }
    }
}
