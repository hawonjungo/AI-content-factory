using AiContentFactory.Domain.Scripts;

namespace AiContentFactory.Application.Scripts;

public record UpsertScriptRequest(
    string Hook,
    string Introduction,
    string Body,
    string Escalation,
    string Payoff,
    string CallToAction);

public record ScriptResponse(
    Guid Id,
    Guid ContentProjectId,
    string Hook,
    string Introduction,
    string Body,
    string Escalation,
    string Payoff,
    string CallToAction,
    DateTimeOffset UpdatedAt)
{
    public static ScriptResponse FromDomain(Script script) => new(
        script.Id,
        script.ContentProjectId,
        script.Hook,
        script.Introduction,
        script.Body,
        script.Escalation,
        script.Payoff,
        script.CallToAction,
        script.UpdatedAt);
}
