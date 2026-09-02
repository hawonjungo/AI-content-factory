namespace AiContentFactory.Domain.Costs;

/// <summary>
/// Tracks individual video generation attempts for Google Flow quota management.
/// Different from AiUsageRecord (USD-based) - this is credit-based.
/// </summary>
public class VideoGenerationRecord
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int CreditsUsed { get; set; }
    public string SceneId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
