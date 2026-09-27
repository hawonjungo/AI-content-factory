using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories.Continuity;

/// <summary>
/// Shared prompt-context helpers used by every <see cref="StoryContinuityManager"/>
/// operation, so each agent's caller doesn't duplicate "flatten this domain
/// object into a truncated block of text" logic. Centralizes the "truncate
/// narrative text before injecting into a prompt" discipline (mirrors
/// AssetReferenceGenerationService.BuildStoryContext's ~2,500-char cap) and
/// the "previous episode ending" derivation.
///
/// IMPORTANT CONTEXT RULE: callers must only ever pass this helper a Story
/// Bible, a Story State, ONE previous episode's Summary (resolved via
/// StoryEpisode.PreviousEpisodeId, one hop only) and the current episode's
/// own Outline - never multiple past episodes' full Script text.
/// </summary>
internal static class ContinuityPromptContext
{
    private const int DefaultMaxChars = 2_500;
    private const int PreviousEndingTailChars = 400;

    public static string Truncate(string? text, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars];
    }

    /// <summary>Flattens a Story Bible into one truncated block of text for prompt injection. Returns an explicit placeholder when no bible has been generated yet.</summary>
    public static string BuildBibleSummary(StoryBible? bible, int maxChars = DefaultMaxChars)
    {
        if (bible is null) return "(no story bible has been generated yet)";

        var parts = new List<string>();
        AddIfPresent(parts, "Premise", bible.Premise);
        AddListIfPresent(parts, "World rules", bible.WorldRules);
        AddIfPresent(parts, "Characters", bible.CharacterDefinitions);
        AddIfPresent(parts, "Character relationships", bible.CharacterRelationships);
        AddListIfPresent(parts, "Visual consistency rules", bible.VisualConsistencyRules);
        AddIfPresent(parts, "Tone", bible.Tone);
        AddListIfPresent(parts, "Recurring elements", bible.RecurringElements);
        AddListIfPresent(parts, "Hard constraints", bible.StoryConstraints);

        return Truncate(string.Join("\n", parts), maxChars);
    }

    public static string BuildVisualConsistencyRules(StoryBible? bible) =>
        bible is null || bible.VisualConsistencyRules.Count == 0
            ? "(none specified)"
            : string.Join("; ", bible.VisualConsistencyRules);

    /// <summary>Flattens an Episode Outline into one truncated block of text for prompt injection.</summary>
    public static string BuildOutlineSummary(StoryEpisodeOutline outline, int maxChars = DefaultMaxChars)
    {
        var parts = new List<string>();
        AddIfPresent(parts, "Title", outline.Title);
        AddIfPresent(parts, "Objective", outline.Objective);
        AddIfPresent(parts, "Setup", outline.Setup);
        AddListIfPresent(parts, "Major beats", outline.MajorBeats);
        AddIfPresent(parts, "Conflict", outline.Conflict);
        AddIfPresent(parts, "Escalation", outline.Escalation);
        AddIfPresent(parts, "Resolution", outline.Resolution);
        AddIfPresent(parts, "Cliffhanger", outline.Cliffhanger);
        AddListIfPresent(parts, "Continuity requirements", outline.ContinuityRequirements);
        AddListIfPresent(parts, "Scenes required", outline.ScenesRequired);

        return Truncate(string.Join("\n", parts), maxChars);
    }

    /// <summary>
    /// Derives a short "how the previous episode ended" cue as the tail of
    /// its Summary, since <see cref="StoryEpisode"/> has no dedicated
    /// "ending" field of its own. Approximate but sufficient context for the
    /// next episode's planner/writer - deliberately avoids loading the
    /// previous episode's full Script.
    /// </summary>
    public static string? DerivePreviousEnding(string? previousEpisodeSummary)
    {
        if (string.IsNullOrWhiteSpace(previousEpisodeSummary)) return null;
        var trimmed = previousEpisodeSummary.Trim();
        return trimmed.Length <= PreviousEndingTailChars ? trimmed : trimmed[^PreviousEndingTailChars..];
    }

    private static void AddIfPresent(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{label}: {value.Trim()}");
    }

    private static void AddListIfPresent(List<string> parts, string label, IReadOnlyList<string> values)
    {
        if (values.Count > 0) parts.Add($"{label}: {string.Join("; ", values)}");
    }
}
