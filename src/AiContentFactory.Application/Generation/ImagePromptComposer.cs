using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// Deterministic still-image prompt composition, extracted from
/// <see cref="FlowGenerationPlanService"/> so its exact "no dialogue, no
/// technical notes, name the character(s) with their real appearance"
/// composition rules exist in exactly one place and are reused verbatim by
/// <c>SceneKeyframeService</c> (the real in-app Keyframe generator) instead
/// of being duplicated. Behaviour is unchanged from before this extraction -
/// <see cref="FlowGenerationPlanService"/>'s own composed text and existing
/// tests are unaffected.
/// </summary>
public static class ImagePromptComposer
{
    /// <param name="action">The scene's own vetted visual action/description text - never raw narration/dialogue.</param>
    /// <param name="behaviorProfiles">
    /// Name -&gt; opted-in <see cref="CharacterBehaviorProfile"/> lookup (e.g.
    /// from <see cref="Stories.IStoryVisualContextResolver.GetCharacterBehaviorProfilesAsync"/>).
    /// Null/missing entries add nothing - a character never gets a behavior
    /// clause unless their own Story "bible" entry explicitly opted in AND
    /// this scene's own <paramref name="action"/> text actually mentions
    /// something that profile reacts to (see <see cref="CharacterBehaviorClauses"/>).
    /// </param>
    public static string Compose(
        string action, string styleGuidance, bool hasCharacterReference,
        IReadOnlyList<string> characterLabels, IReadOnlyDictionary<string, string>? visualDescriptions,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles = null,
        Domain.Storyboards.ShotSize shot = Domain.Storyboards.ShotSize.Unspecified)
    {
        var character = BuildCharacterInstruction(hasCharacterReference, characterLabels, visualDescriptions);
        var behavior = BuildBehaviorInstruction(characterLabels, behaviorProfiles, action);
        // Framing, when known, is stated before the subject (e.g. "..., close-up: ...").
        var framing = VideoPromptBuilder.ShotToText(shot) is { } shotText ? $", {char.ToLowerInvariant(shotText[0])}{shotText[1..]}" : string.Empty;
        // The action usually arrives as a full sentence - drop its own stop so it never reads "..".
        return $"A photorealistic vertical 9:16 photograph{framing}: {action.TrimEnd().TrimEnd('.')}.{character}{behavior} Visual style: {styleGuidance}. Deliberate framing, clear focal subject, filmic lighting.";
    }

    /// <summary>
    /// The character-consistency clause - named with each character's actual
    /// resolved appearance (e.g. "Milo (a small orange tabby cat with round
    /// green eyes)") when known. Falls back to a species-neutral generic
    /// instruction when a Character reference exists but no name/description
    /// is known (legacy/non-Story project, single generic Character anchor).
    /// Empty when no Character reference is relevant at all.
    /// </summary>
    public static string BuildCharacterInstruction(
        bool hasCharacterReference, IReadOnlyList<string> characterLabels, IReadOnlyDictionary<string, string>? visualDescriptions)
    {
        if (characterLabels.Count > 0)
        {
            var clauses = characterLabels.Select(name =>
            {
                var description = visualDescriptions is not null
                    && visualDescriptions.TryGetValue(name, out var d)
                    && !string.IsNullOrWhiteSpace(d)
                        ? d.Trim()
                        : null;
                return description is null ? name : $"{name} ({description})";
            }).ToList();

            var joined = clauses.Count switch
            {
                1 => clauses[0],
                2 => $"{clauses[0]} and {clauses[1]}",
                _ => string.Join(", ", clauses.Take(clauses.Count - 1)) + " and " + clauses[^1],
            };

            return $" Feature {joined}, exactly as shown in the attached reference image(s).";
        }

        return hasCharacterReference
            ? " Feature the main character exactly as shown in the attached reference image."
            : string.Empty;
    }

    /// <summary>
    /// Per-character behavior clauses (space-joined, same style as the
    /// character-consistency clause above) - empty unless at least one of
    /// <paramref name="characterLabels"/> both opted into a non-<see cref="CharacterBehaviorProfile.None"/>
    /// profile in <paramref name="behaviorProfiles"/> AND has a matching cue
    /// in <paramref name="action"/> (see <see cref="CharacterBehaviorClauses.For"/>).
    /// A character with no entry in <paramref name="behaviorProfiles"/> - the
    /// default for every non-opted-in character, every non-cat character, and
    /// every non-Story project - never contributes anything here.
    /// Deduplicated (<see cref="Enumerable.Distinct{T}(IEnumerable{T})"/>): the
    /// clause text is species-generic, not per-character-named, so two
    /// opted-in characters sharing the same profile and the same scene action
    /// (e.g. Milo and Mimi both AnthropomorphicCat in one shared shot) would
    /// otherwise produce the identical sentence twice.
    /// </summary>
    private static string BuildBehaviorInstruction(
        IReadOnlyList<string> characterLabels, IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles, string action)
    {
        if (behaviorProfiles is null || characterLabels.Count == 0)
        {
            return string.Empty;
        }

        var clauses = characterLabels
            .Where(name => behaviorProfiles.TryGetValue(name, out var profile) && profile != CharacterBehaviorProfile.None)
            .Select(name => CharacterBehaviorClauses.For(behaviorProfiles[name], action))
            .Where(clause => clause is not null)
            .Select(clause => clause!)
            .Distinct()
            .ToList();

        return clauses.Count == 0 ? string.Empty : " " + string.Join(" ", clauses);
    }
}
