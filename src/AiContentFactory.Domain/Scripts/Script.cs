using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.Scripts;

/// <summary>
/// Structured script for a ContentProject. Kept as discrete fields (not one
/// text blob) so later analytics can correlate e.g. hook wording with
/// retention. Populated by the Script Agent starting Phase 3; empty/blank
/// fields are valid in Phase 1-2 while the project is edited manually.
/// </summary>
public class Script : BaseEntity
{
    public Guid ContentProjectId { get; private set; }
    public string Hook { get; private set; } = string.Empty;
    public string Introduction { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string Escalation { get; private set; } = string.Empty;
    public string Payoff { get; private set; } = string.Empty;
    public string CallToAction { get; private set; } = string.Empty;

    private Script()
    {
        // EF Core
    }

    public static Script Create(Guid contentProjectId) => new()
    {
        ContentProjectId = contentProjectId
    };

    public void UpdateSections(string hook, string introduction, string body, string escalation, string payoff, string callToAction)
    {
        Hook = hook ?? string.Empty;
        Introduction = introduction ?? string.Empty;
        Body = body ?? string.Empty;
        Escalation = escalation ?? string.Empty;
        Payoff = payoff ?? string.Empty;
        CallToAction = callToAction ?? string.Empty;
        Touch();
    }
}
