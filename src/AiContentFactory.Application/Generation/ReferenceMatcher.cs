using AiContentFactory.Domain.AssetReferences;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// The single per-scene "which named references apply here" algorithm,
/// shared by the direct Veo path (<see cref="SceneAssetGenerator"/>) and the
/// Google Flow copy-paste export (<see cref="FlowGenerationPlanService"/>) so
/// the tag-matching + Veo's 3-reference cap + Environment-first priority rule
/// only exists once. Generic over whatever "one approved reference row" shape
/// each caller already has (an <see cref="ApprovedSceneReference"/> pairing a
/// <see cref="ReferenceImage"/> for Veo, a raw <c>AssetReference</c> entity
/// for the Flow plan) via two cheap property selectors, so neither caller has
/// to convert its own shape just to reuse this.
/// </summary>
public static class ReferenceMatcher
{
    private const int MaxReferences = 3;

    /// <summary>
    /// Scene-tagged names (<c>Scene.RelevantReferenceLabels</c>) matched
    /// against a project's approved references by exact (case-insensitive)
    /// Label equality, then capped/prioritized to at most 3 - see
    /// <see cref="CapAndPrioritize{T}"/>. Returns an empty list (never null)
    /// whenever there are no tags or nothing matches, so callers can fall
    /// back to their own legacy Character+Environment pair unchanged.
    /// </summary>
    public static IReadOnlyList<T> Match<T>(
        IReadOnlyList<T> approved,
        IReadOnlyList<string> taggedLabels,
        string narration,
        Func<T, AssetReferenceType> typeOf,
        Func<T, string?> labelOf)
    {
        if (taggedLabels.Count == 0)
        {
            return Array.Empty<T>();
        }

        var matched = approved
            .Where(candidate => labelOf(candidate) is string label
                && taggedLabels.Any(tag => string.Equals(tag, label, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return matched.Count == 0 ? Array.Empty<T>() : CapAndPrioritize(matched, narration, typeOf, labelOf);
    }

    /// <summary>
    /// Veo accepts at most 3 reference images per call
    /// (<c>VeoVideoProvider</c>'s <c>.Take(3)</c>). When more than 3 named
    /// references match a scene (e.g. 2 characters + 2 locations), priority
    /// is: 1) matched Environment/location reference(s) first - the scene's
    /// setting matters most for visual grounding - then 2) matched Character
    /// references, ordered by where their name first appears in the scene's
    /// narration (an earlier mention reads as more central to the scene),
    /// filling whatever budget remains up to 3 total. This ordering is a
    /// judgment call, not a hard product requirement.
    /// </summary>
    private static IReadOnlyList<T> CapAndPrioritize<T>(
        List<T> matched, string narration, Func<T, AssetReferenceType> typeOf, Func<T, string?> labelOf)
    {
        if (matched.Count <= MaxReferences)
        {
            return matched;
        }

        return matched
            .Where(candidate => typeOf(candidate) == AssetReferenceType.Environment)
            .Concat(matched
                .Where(candidate => typeOf(candidate) != AssetReferenceType.Environment)
                .OrderBy(candidate => NarrationIndexOf(narration, labelOf(candidate))))
            .Take(MaxReferences)
            .ToList();
    }

    private static int NarrationIndexOf(string narration, string? label)
    {
        if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(narration))
        {
            return int.MaxValue;
        }

        var index = narration.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>
    /// Splits a matched set into character names (for the direct-path
    /// prompt's "name them" sentence / the Flow export's [CHARACTER REF]
    /// line) and at most one location name (for the direct-path prompt's
    /// setting sentence / the Flow export's [LOCATION] line) - a scene has
    /// one setting even if more than one Environment row happened to match.
    /// </summary>
    public static (IReadOnlyList<string> CharacterLabels, string? LocationLabel) SplitNames<T>(
        IReadOnlyList<T> matched, Func<T, AssetReferenceType> typeOf, Func<T, string?> labelOf)
    {
        var characterLabels = matched
            .Where(candidate => typeOf(candidate) == AssetReferenceType.Character)
            .Select(labelOf)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var locationLabel = matched
            .Where(candidate => typeOf(candidate) == AssetReferenceType.Environment)
            .Select(labelOf)
            .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))
            ?.Trim();

        return (characterLabels, locationLabel);
    }
}
