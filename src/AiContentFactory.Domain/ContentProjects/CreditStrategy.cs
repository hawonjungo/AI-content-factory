namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// How aggressively a project spends its AI-video credit budget when the
/// storyboard is allocated. Steers <c>VideoAllocationPlanner</c>: it never
/// pins AI video to fixed timestamps, it changes how many scenes clear the bar
/// for a clip.
/// </summary>
public enum CreditStrategy
{
    /// <summary>Hero clip plus the strongest supporting beats within budget (the 20 + 10 + 10 + 10 shape).</summary>
    Balanced = 0,

    /// <summary>Spend the whole budget on video wherever it plausibly helps.</summary>
    MaxImpact = 1,

    /// <summary>Video only on the single hero moment; everything else is a still.</summary>
    Economy = 2
}
