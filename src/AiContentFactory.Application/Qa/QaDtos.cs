using AiContentFactory.Domain.Qa;

namespace AiContentFactory.Application.Qa;

public record QaScoreResponse(
    Guid Id,
    Guid ContentProjectId,
    double Hook,
    double Story,
    double Pacing,
    double VisualQuality,
    double AudioQuality,
    double SubtitleQuality,
    double Consistency,
    double FactualAccuracy,
    double PlatformSuitability,
    double Overall,
    string Notes,
    DateTimeOffset CreatedAt)
{
    public static QaScoreResponse FromDomain(QaScore score) => new(
        score.Id, score.ContentProjectId, score.Hook, score.Story, score.Pacing, score.VisualQuality,
        score.AudioQuality, score.SubtitleQuality, score.Consistency, score.FactualAccuracy,
        score.PlatformSuitability, score.Overall, score.Notes, score.CreatedAt);
}
