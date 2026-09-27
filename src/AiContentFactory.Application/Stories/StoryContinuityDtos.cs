using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

/// <summary>
/// Request/response DTOs for the AI-generation endpoints layered on top of
/// the Story/Series module (bible/outline/script/validate/finalize) - kept
/// separate from <c>StoryDtos.cs</c>'s plain CRUD shapes. Mirrors the
/// existing <c>FromDomain(...)</c> factory convention.
/// </summary>
public record PlanStoryBibleRequest(
    string? Theme,
    string? Genre,
    string? Tone,
    string? TargetAudience,
    IReadOnlyList<string>? MainCharacters,
    IReadOnlyList<string>? Locations,
    string? StoryRulesConstraints,
    int? DesiredEpisodeCount);

/// <summary>
/// Plain CRUD bible edit (PUT .../bible) - no AI call, distinct from
/// POST .../bible (<see cref="Continuity.IStoryContinuityManager.PlanStoryAsync"/>)
/// which always (re)generates the whole Bible via the planner agent and would
/// unnecessarily spend an LLM call (and risk drifting unrelated fields) for a
/// human correcting one fact - e.g. fixing a stale/contradictory
/// VisualConsistencyRules entry. Mirrors every field on <see cref="StoryBibleResponse"/>;
/// replaces the Bible wholesale, same as <see cref="Domain.Stories.Story.SetBible"/>.
/// </summary>
public record UpdateStoryBibleRequest(
    string? Premise,
    IReadOnlyList<string>? WorldRules,
    string? CharacterDefinitions,
    string? CharacterRelationships,
    IReadOnlyList<string>? VisualConsistencyRules,
    string? Tone,
    IReadOnlyList<string>? RecurringElements,
    IReadOnlyList<string>? StoryConstraints,
    string? StoryArc);

public record StoryBibleResponse(
    string? Premise,
    IReadOnlyList<string> WorldRules,
    string? CharacterDefinitions,
    string? CharacterRelationships,
    IReadOnlyList<string> VisualConsistencyRules,
    string? Tone,
    IReadOnlyList<string> RecurringElements,
    IReadOnlyList<string> StoryConstraints,
    string? StoryArc)
{
    public static StoryBibleResponse FromDomain(StoryBible bible) => new(
        bible.Premise,
        bible.WorldRules,
        bible.CharacterDefinitions,
        bible.CharacterRelationships,
        bible.VisualConsistencyRules,
        bible.Tone,
        bible.RecurringElements,
        bible.StoryConstraints,
        bible.StoryArc);
}

public record StoryEpisodeOutlineResponse(
    Guid EpisodeId,
    string? Title,
    string? Objective,
    string? Setup,
    IReadOnlyList<string> MajorBeats,
    string? Conflict,
    string? Escalation,
    string? Resolution,
    string? Cliffhanger,
    IReadOnlyList<string> ContinuityRequirements,
    IReadOnlyList<string> ScenesRequired)
{
    public static StoryEpisodeOutlineResponse FromDomain(Guid episodeId, StoryEpisodeOutline outline) => new(
        episodeId,
        outline.Title,
        outline.Objective,
        outline.Setup,
        outline.MajorBeats,
        outline.Conflict,
        outline.Escalation,
        outline.Resolution,
        outline.Cliffhanger,
        outline.ContinuityRequirements,
        outline.ScenesRequired);
}

public record ContinuityIssueResponse(string Category, string Type, string Message)
{
    public static ContinuityIssueResponse FromDomain(ContinuityIssue issue) => new(
        NormalizeCategory(issue.Category),
        NormalizeType(issue.Type),
        issue.Message?.Trim() ?? string.Empty);

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? ContinuityCategories.Other : category.Trim();

    private static string NormalizeType(string? type) =>
        string.IsNullOrWhiteSpace(type) ? ContinuityIssueTypes.Other : type.Trim().ToUpperInvariant();
}

/// <summary>
/// Used both standalone (POST .../validate) and nested inside
/// <see cref="FinalizeEpisodeResponse"/>. <see cref="Valid"/> is computed
/// deterministically here from <see cref="CriticalIssues"/> - the LLM's own
/// output carries no "valid" boolean, so there is nothing to disagree with.
/// A Warnings-only result (no CriticalIssues) is Valid: true - warnings are
/// informational only and never block finalization on their own.
/// </summary>
public record ContinuityValidationResponse(bool Valid, double Score, IReadOnlyList<ContinuityIssueResponse> Warnings, IReadOnlyList<ContinuityIssueResponse> CriticalIssues)
{
    public static ContinuityValidationResponse FromOutput(ContinuityValidationOutput output)
    {
        var criticalIssues = (output.CriticalIssues ?? new List<ContinuityIssue>()).Select(ContinuityIssueResponse.FromDomain).ToList();
        var warnings = (output.Warnings ?? new List<ContinuityIssue>()).Select(ContinuityIssueResponse.FromDomain).ToList();

        return new ContinuityValidationResponse(
            Valid: criticalIssues.Count == 0,
            Score: Math.Clamp(output.Score, 0.0, 1.0),
            Warnings: warnings,
            CriticalIssues: criticalIssues);
    }
}

/// <summary>
/// Response for POST .../finalize. Always 200 OK: <see cref="Valid"/> false
/// means continuity validation blocked finalization (StoryState was NOT
/// mutated, the episode was NOT completed - <see cref="Episode"/> is null so
/// callers don't mistake it for the completed episode); true means the
/// episode is now Completed and its snapshot reflects the new state. A
/// Valid: true result may still carry non-empty <see cref="Warnings"/> - the
/// UI can surface them, but they never block finalization by themselves.
/// </summary>
public record FinalizeEpisodeResponse(
    bool Valid,
    double Score,
    IReadOnlyList<ContinuityIssueResponse> Warnings,
    IReadOnlyList<ContinuityIssueResponse> CriticalIssues,
    StoryEpisodeResponse? Episode);
