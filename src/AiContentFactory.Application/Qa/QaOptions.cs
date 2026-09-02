namespace AiContentFactory.Application.Qa;

public class QaOptions
{
    public const string SectionName = "Qa";

    /// <summary>Overall score (0-10) at/above which a project moves to AwaitingApproval instead of back to Editing.</summary>
    public double MinimumOverallScoreToProceed { get; set; } = 6.0;
}
