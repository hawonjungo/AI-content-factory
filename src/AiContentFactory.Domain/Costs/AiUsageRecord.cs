using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.Costs;

/// <summary>
/// One row per billable AI operation. Deliberately simple (no aggregate
/// behavior) - it's a ledger entry, written once and never mutated.
/// </summary>
public class AiUsageRecord : BaseEntity
{
    public Guid? ContentProjectId { get; private set; }
    public Guid? SceneId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public decimal EstimatedCostUsd { get; private set; }

    private AiUsageRecord()
    {
        // EF Core
    }

    public static AiUsageRecord Create(string provider, string model, string operation, decimal estimatedCostUsd, Guid? contentProjectId, Guid? sceneId) => new()
    {
        Provider = provider,
        Model = model,
        Operation = operation,
        EstimatedCostUsd = estimatedCostUsd,
        ContentProjectId = contentProjectId,
        SceneId = sceneId
    };
}
