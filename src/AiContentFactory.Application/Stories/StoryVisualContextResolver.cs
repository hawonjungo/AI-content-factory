using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

/// <summary>
/// Resolves the short Story-continuity blurb that feeds
/// <see cref="Agents.PromptAgentInput.StoryVisualContext"/>, for the two
/// call sites that build a scene's <see cref="Agents.PromptAgentInput"/>
/// (<see cref="Storyboards.StoryboardService.SuggestScenePromptAsync"/> and
/// <see cref="Generation.SceneAssetGenerator"/>'s prompt resolution). Kept as
/// its own small resolver (rather than duplicating the reverse-lookup +
/// truncation logic in both places) so the two call sites can't drift.
/// </summary>
public interface IStoryVisualContextResolver
{
    /// <summary>
    /// Null when the ContentProject isn't linked to a Story episode - the
    /// normal/default case for every non-Story project, which leaves
    /// PromptAgentInput.StoryVisualContext (and therefore the generated
    /// prompt) unchanged from before this feature existed.
    /// </summary>
    Task<string?> ResolveAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Story's character + location names (from its "bible"), for
    /// deterministic no-AI name matching against a scene's own text - see
    /// <see cref="Storyboards.ClipPlanService"/>. Null when the ContentProject
    /// isn't linked to a Story episode, same "unaffected by default" contract
    /// as <see cref="ResolveAsync"/>.
    /// </summary>
    Task<IReadOnlyList<string>?> GetStoryCastAndLocationNamesAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Name -&gt; appearance-description lookup for the Story's cast/locations
    /// that actually have a <c>VisualDescription</c> on their "bible" entry
    /// (e.g. <c>"Milo" -&gt; "orange tabby, one white paw, green eyes"</c>) -
    /// for the Flow copy-paste export's <c>[CHARACTER REF]</c>/<c>[LOCATION]</c>
    /// lines, which travel as plain text with no reference image attached, so
    /// a bare name is not enough. Case-insensitive keys. Entries with no
    /// <c>VisualDescription</c> are simply absent (never an empty value).
    /// Null under the same "not a Story-linked project" contract as
    /// <see cref="ResolveAsync"/>/<see cref="GetStoryCastAndLocationNamesAsync"/>,
    /// and also when the project IS Story-linked but nothing has a
    /// VisualDescription yet.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>?> GetCastAndLocationVisualDescriptionsAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Name -&gt; opted-in <see cref="CharacterBehaviorProfile"/> lookup, for
    /// deterministic scoped behavioral prompt clauses (e.g. an
    /// anthropomorphic-cat character) - see <see cref="Generation.CharacterBehaviorClauses"/>.
    /// Case-insensitive keys. Built ONLY from <c>StoryCharacter</c> rows whose
    /// <see cref="CharacterBehaviorProfile"/> is explicitly non-<see cref="CharacterBehaviorProfile.None"/> -
    /// never inferred from name/niche/keywords. Entries with
    /// <see cref="CharacterBehaviorProfile.None"/> (the default for every
    /// character) are simply absent. Null under the same "not a Story-linked
    /// project" contract as <see cref="ResolveAsync"/>/
    /// <see cref="GetCastAndLocationVisualDescriptionsAsync"/>, and also when
    /// the project IS Story-linked but no character opted into a profile yet.
    /// </summary>
    Task<IReadOnlyDictionary<string, CharacterBehaviorProfile>?> GetCharacterBehaviorProfilesAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class StoryVisualContextResolver : IStoryVisualContextResolver
{
    /// <summary>
    /// How many of the episode's <see cref="StoryStateSnapshot.ImportantEvents"/>
    /// to surface - this is scene-generation guidance, not a full recap, so
    /// only the most recent few matter.
    /// </summary>
    private const int RecentEventsCap = 3;

    private readonly IStoryRepository _storyRepository;

    public StoryVisualContextResolver(IStoryRepository storyRepository)
    {
        _storyRepository = storyRepository;
    }

    public async Task<string?> ResolveAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveEpisodeAndStoryAsync(contentProjectId, cancellationToken);
        if (resolved is not { } found)
        {
            return null;
        }

        var (episode, story) = found;

        var parts = new List<string>();

        var visualRules = BuildFilteredVisualConsistencyRules(story);
        if (visualRules is not null)
        {
            parts.Add($"Visual consistency rules: {visualRules}");
        }

        var castVisuals = story.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.VisualDescription))
            .Select(c => $"{c.Name}: {c.VisualDescription}")
            .ToList();
        if (castVisuals.Count > 0)
        {
            parts.Add($"Cast appearance: {string.Join("; ", castVisuals)}");
        }

        var locationVisuals = story.Locations
            .Where(l => !string.IsNullOrWhiteSpace(l.VisualDescription))
            .Select(l => $"{l.Name}: {l.VisualDescription}")
            .ToList();
        if (locationVisuals.Count > 0)
        {
            parts.Add($"Locations: {string.Join("; ", locationVisuals)}");
        }

        // The episode's own frozen StoryStateSnapshot (as of THIS episode),
        // NOT the Story's current live State - a not-yet-completed episode
        // has no canonical state yet, and that's fine, not an error: just
        // skip this section rather than substituting the live state.
        if (episode.StoryStateSnapshot is { } snapshot)
        {
            var stateLine = BuildStateLine(snapshot);
            if (stateLine is not null)
            {
                parts.Add(stateLine);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return ContinuityPromptContext.Truncate(string.Join(" | ", parts));
    }

    public async Task<IReadOnlyList<string>?> GetStoryCastAndLocationNamesAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveEpisodeAndStoryAsync(contentProjectId, cancellationToken);
        if (resolved is not { } found)
        {
            return null;
        }

        var (_, story) = found;

        return story.Characters.Select(c => c.Name)
            .Concat(story.Locations.Select(l => l.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetCastAndLocationVisualDescriptionsAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveEpisodeAndStoryAsync(contentProjectId, cancellationToken);
        if (resolved is not { } found)
        {
            return null;
        }

        var (_, story) = found;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var character in story.Characters)
        {
            if (!string.IsNullOrWhiteSpace(character.Name) && !string.IsNullOrWhiteSpace(character.VisualDescription))
            {
                map[character.Name.Trim()] = character.VisualDescription!.Trim();
            }
        }

        foreach (var location in story.Locations)
        {
            if (!string.IsNullOrWhiteSpace(location.Name) && !string.IsNullOrWhiteSpace(location.VisualDescription))
            {
                map[location.Name.Trim()] = location.VisualDescription!.Trim();
            }
        }

        return map.Count == 0 ? null : map;
    }

    public async Task<IReadOnlyDictionary<string, CharacterBehaviorProfile>?> GetCharacterBehaviorProfilesAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveEpisodeAndStoryAsync(contentProjectId, cancellationToken);
        if (resolved is not { } found)
        {
            return null;
        }

        var (_, story) = found;

        var map = new Dictionary<string, CharacterBehaviorProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var character in story.Characters.Where(c => c.BehaviorProfile != CharacterBehaviorProfile.None))
        {
            if (!string.IsNullOrWhiteSpace(character.Name))
            {
                map[character.Name.Trim()] = character.BehaviorProfile;
            }
        }

        return map.Count == 0 ? null : map;
    }

    /// <summary>
    /// Filters the Bible's free-form visual-consistency rules to drop any
    /// rule that names a character/location which already has its own
    /// canonical <c>VisualDescription</c> - the same fix already applied in
    /// <see cref="StoryAssetReferenceService.AppendBibleGuidance"/> for the
    /// reference-image prompt, applied here for this resolver's per-scene
    /// prompt path (<see cref="Agents.PromptAgentInput.StoryVisualContext"/>).
    /// A stale or LLM-drifted Bible rule (e.g. "Mimi must always be a white
    /// cat") must never sit in the same prompt as - and contradict - the
    /// character's own, authoritative VisualDescription (e.g. "grey
    /// short-haired"): see that type's own remarks for the real incident this
    /// guards against. Returns null when there is no Bible, no rules, or
    /// every rule got excluded.
    /// </summary>
    private static string? BuildFilteredVisualConsistencyRules(Story story)
    {
        if (story.Bible is not { VisualConsistencyRules.Count: > 0 } bible)
        {
            return null;
        }

        var namesWithOwnDescription = story.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.VisualDescription))
            .Select(c => c.Name)
            .Concat(story.Locations.Where(l => !string.IsNullOrWhiteSpace(l.VisualDescription)).Select(l => l.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        var rules = bible.VisualConsistencyRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule)
                && !namesWithOwnDescription.Any(name => rule.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return rules.Count == 0 ? null : string.Join("; ", rules);
    }

    /// <summary>
    /// The reverse lookup (ContentProject -> Episode -> Story) shared by
    /// <see cref="ResolveAsync"/>, <see cref="GetStoryCastAndLocationNamesAsync"/>
    /// and <see cref="GetCastAndLocationVisualDescriptionsAsync"/> so it exists
    /// in exactly one place. Null in either step means "not a Story-linked
    /// project" (or a dangling episode), same contract every caller relies on.
    /// </summary>
    private async Task<(StoryEpisode Episode, Story Story)?> ResolveEpisodeAndStoryAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var episode = await _storyRepository.GetEpisodeByContentProjectIdAsync(contentProjectId, cancellationToken);
        if (episode is null)
        {
            return null;
        }

        var story = await _storyRepository.GetByIdAsync(episode.StoryId, cancellationToken);
        if (story is null)
        {
            return null;
        }

        return (episode, story);
    }

    /// <summary>
    /// Flattens an episode's <see cref="StoryStateSnapshot"/> into one short
    /// "current scene setting/objective/recent events" line, or null when the
    /// snapshot carries nothing usable (all fields blank).
    /// </summary>
    private static string? BuildStateLine(StoryStateSnapshot snapshot)
    {
        var segments = new List<string>();

        if (!string.IsNullOrWhiteSpace(snapshot.CurrentLocation))
        {
            segments.Add($"Current scene setting: {snapshot.CurrentLocation.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.CurrentObjective))
        {
            segments.Add($"Current objective: {snapshot.CurrentObjective.Trim()}");
        }

        var recentEvents = snapshot.ImportantEvents.TakeLast(RecentEventsCap).ToList();
        if (recentEvents.Count > 0)
        {
            segments.Add($"Recent events: {string.Join("; ", recentEvents)}");
        }

        return segments.Count == 0 ? null : string.Join(". ", segments) + ".";
    }
}
