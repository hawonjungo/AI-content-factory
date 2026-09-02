using AiContentFactory.Domain.Generation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Costs;

/// <inheritdoc cref="ICreditLedger"/>
public class CreditLedgerService : ICreditLedger
{
    private static readonly GenerationAttemptStatus[] CountsAsSpend =
    {
        GenerationAttemptStatus.Pending,
        GenerationAttemptStatus.Generating,
        GenerationAttemptStatus.Retrying,
        GenerationAttemptStatus.Completed,
        GenerationAttemptStatus.Validated
    };

    private static readonly GenerationAttemptStatus[] Succeeded =
    {
        GenerationAttemptStatus.Completed,
        GenerationAttemptStatus.Validated
    };

    private readonly IGenerationAttemptRepository _repository;
    private readonly CreditCostOptions _costs;
    private readonly ILogger<CreditLedgerService> _logger;

    public CreditLedgerService(
        IGenerationAttemptRepository repository,
        IOptions<CreditCostOptions> costs,
        ILogger<CreditLedgerService> logger)
    {
        _repository = repository;
        _costs = costs.Value;
        _logger = logger;
    }

    public async Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default)
    {
        var (spent, _, _) = await BreakdownTodayAsync(cancellationToken);
        return Math.Max(0, _costs.DailyBudgetCredits - spent);
    }

    public async Task<CreditUsageSummary> GetDailyUsageAsync(CancellationToken cancellationToken = default)
    {
        var (spent, reserved, used) = await BreakdownTodayAsync(cancellationToken);
        var failed = await FailedTodayAsync(cancellationToken);
        return new CreditUsageSummary(
            DailyBudget: _costs.DailyBudgetCredits,
            Reserved: reserved,
            Used: used,
            Remaining: Math.Max(0, _costs.DailyBudgetCredits - spent),
            FailedToday: failed);
    }

    public async Task EnsureAvailableAsync(int requiredCredits, CancellationToken cancellationToken = default)
    {
        if (requiredCredits <= 0)
        {
            return;
        }

        var remaining = await GetRemainingDailyCreditsAsync(cancellationToken);
        if (requiredCredits > remaining)
        {
            throw new CreditBudgetExceededException(requiredCredits, remaining, _costs.DailyBudgetCredits);
        }
    }

    public async Task<GenerationAttempt> ReserveAsync(CreditReservationRequest request, CancellationToken cancellationToken = default)
    {
        var priorAttempts = request.SceneId is { } sceneId
            ? await _repository.GetForSceneAsync(request.ContentProjectId, sceneId, request.Kind, cancellationToken)
            : Array.Empty<GenerationAttempt>();

        // Idempotency: a scene+kind that already succeeded is not generated (or
        // charged) again.
        var alreadyDone = priorAttempts.FirstOrDefault(a => Succeeded.Contains(a.Status));
        if (alreadyDone is not null)
        {
            _logger.LogInformation(
                "Credit reserve skipped: {Kind} for scene {SceneId} already {Status} (attempt {AttemptId})",
                request.Kind, request.SceneId, alreadyDone.Status, alreadyDone.Id);
            return alreadyDone;
        }

        // Retry protection: stop once the failed attempts hit the cap.
        var failedCount = priorAttempts.Count(a => a.Status == GenerationAttemptStatus.Failed);
        if (failedCount >= _costs.MaxAttemptsPerScene)
        {
            throw new RetryLimitExceededException(request.SceneId, request.Kind.ToString(), failedCount, _costs.MaxAttemptsPerScene);
        }

        var estimatedCredits = _costs.CreditsFor(request.Kind, request.Tier);
        await EnsureAvailableAsync(estimatedCredits, cancellationToken);

        var attemptNumber = priorAttempts.Count + 1;
        var attempt = GenerationAttempt.Reserve(
            request.ContentProjectId,
            request.SceneId,
            request.Kind,
            request.Provider,
            request.Model,
            request.Tier?.ToString(),
            estimatedCredits,
            attemptNumber);

        if (failedCount > 0)
        {
            attempt.MarkRetrying();
        }
        else
        {
            attempt.MarkGenerating();
        }

        await _repository.AddAsync(attempt, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reserved {Credits} credit(s) for {Kind} (attempt {AttemptNumber}) on project {ProjectId} scene {SceneId}",
            estimatedCredits, request.Kind, attemptNumber, request.ContentProjectId, request.SceneId);

        return attempt;
    }

    public async Task CommitAsync(Guid attemptId, int actualCredits, Guid? assetId = null, double? audioDurationSeconds = null, CancellationToken cancellationToken = default)
    {
        var attempt = await Load(attemptId, cancellationToken);
        attempt.MarkCompleted(actualCredits, assetId, audioDurationSeconds);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<GenerationAttempt> RecordExternalCompletionAsync(
        CreditReservationRequest request,
        int actualCredits,
        Guid? assetId = null,
        CancellationToken cancellationToken = default)
    {
        if (request.SceneId is { } sceneId)
        {
            var prior = await _repository.GetForSceneAsync(request.ContentProjectId, sceneId, request.Kind, cancellationToken);
            var alreadyDone = prior.FirstOrDefault(a => Succeeded.Contains(a.Status));
            if (alreadyDone is not null)
            {
                _logger.LogInformation(
                    "External {Kind} completion for scene {SceneId} already recorded (attempt {AttemptId})",
                    request.Kind, sceneId, alreadyDone.Id);
                return alreadyDone;
            }
        }

        var credits = Math.Max(0, actualCredits);
        var attempt = GenerationAttempt.Reserve(
            request.ContentProjectId, request.SceneId, request.Kind,
            request.Provider, request.Model, request.Tier?.ToString(),
            estimatedCredits: credits, attemptNumber: 1);
        attempt.MarkGenerating();
        attempt.MarkCompleted(credits, assetId, audioDurationSeconds: null);

        await _repository.AddAsync(attempt, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded {Credits} externally-consumed credit(s) for {Kind} on scene {SceneId} (project {ProjectId})",
            credits, request.Kind, request.SceneId, request.ContentProjectId);

        return attempt;
    }

    public async Task MarkValidatedAsync(Guid attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await Load(attemptId, cancellationToken);
        attempt.MarkValidated();
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid attemptId, string reason, CancellationToken cancellationToken = default)
    {
        var attempt = await Load(attemptId, cancellationToken);
        attempt.MarkFailed(reason);
        await _repository.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Generation attempt {AttemptId} failed: {Reason}", attemptId, reason);
    }

    private async Task<GenerationAttempt> Load(Guid attemptId, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(attemptId, cancellationToken)
        ?? throw new InvalidOperationException($"GenerationAttempt '{attemptId}' was not found.");

    /// <summary>(totalSpent, reserved, used) for attempts created today.</summary>
    private async Task<(int Spent, int Reserved, int Used)> BreakdownTodayAsync(CancellationToken cancellationToken)
    {
        var startOfDayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var attempts = await _repository.GetSinceAsync(startOfDayUtc, cancellationToken);

        var reserved = 0;
        var used = 0;
        foreach (var attempt in attempts)
        {
            if (Succeeded.Contains(attempt.Status))
            {
                used += attempt.ActualCredits ?? attempt.EstimatedCredits;
            }
            else if (CountsAsSpend.Contains(attempt.Status))
            {
                reserved += attempt.EstimatedCredits;
            }
        }

        return (reserved + used, reserved, used);
    }

    private async Task<int> FailedTodayAsync(CancellationToken cancellationToken)
    {
        var startOfDayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var attempts = await _repository.GetSinceAsync(startOfDayUtc, cancellationToken);
        return attempts.Count(a => a.Status == GenerationAttemptStatus.Failed);
    }
}
