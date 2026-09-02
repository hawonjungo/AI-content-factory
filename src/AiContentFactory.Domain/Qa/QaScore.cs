using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.Qa;

/// <summary>
/// One QA run's scores (0-10 each dimension). Immutable - re-running QA
/// creates a new row rather than overwriting, so score history is kept.
/// </summary>
public class QaScore : BaseEntity
{
    public Guid ContentProjectId { get; private set; }
    public double Hook { get; private set; }
    public double Story { get; private set; }
    public double Pacing { get; private set; }
    public double VisualQuality { get; private set; }
    public double AudioQuality { get; private set; }
    public double SubtitleQuality { get; private set; }
    public double Consistency { get; private set; }
    public double FactualAccuracy { get; private set; }
    public double PlatformSuitability { get; private set; }
    public double Overall { get; private set; }
    public string Notes { get; private set; } = string.Empty;

    private QaScore()
    {
        // EF Core
    }

    public static QaScore Create(
        Guid contentProjectId, double hook, double story, double pacing, double visualQuality,
        double audioQuality, double subtitleQuality, double consistency, double factualAccuracy,
        double platformSuitability, double overall, string notes) => new()
    {
        ContentProjectId = contentProjectId,
        Hook = hook,
        Story = story,
        Pacing = pacing,
        VisualQuality = visualQuality,
        AudioQuality = audioQuality,
        SubtitleQuality = subtitleQuality,
        Consistency = consistency,
        FactualAccuracy = factualAccuracy,
        PlatformSuitability = platformSuitability,
        Overall = overall,
        Notes = notes ?? string.Empty
    };
}
