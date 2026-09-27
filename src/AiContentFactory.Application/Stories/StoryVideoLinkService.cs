using System.Text.RegularExpressions;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiContentFactory.Application.Stories;

/// <summary>Result of <see cref="IStoryVideoLinkService.CreateOrOpenVideoAsync"/>. <see cref="Created"/> tells the caller whether a brand new ContentProject was just created ("Create Video") or an already-linked one was simply returned ("Open Video").</summary>
public record StoryVideoLinkResponse(Guid ContentProjectId, bool Created);

/// <summary>
/// Result of <see cref="IStoryVideoLinkService.SyncStoryReferencesAsync"/>.
/// <see cref="SyncedLabels"/> lists which NAMED references were newly seeded
/// by this call, formatted as <c>"{Type}: {Label}"</c> (e.g. "Character: Milo",
/// "Environment: Ha Long Bay") - or just the bare type name for the rare
/// legacy case of a null-label seed. Empty when there was nothing to do
/// (every Story-level named character/location already has a matching
/// Approved reference on this project, or the Story has no approved image
/// for whatever is still missing).
/// <see cref="OutdatedLabels"/> lists the NAMES of characters whose Approved
/// reference on this project points at a different image than the Story's
/// current approved one (the Story-level image was replaced after this
/// episode's project was created). Detection only - sync never overwrites an
/// approved project reference, so nothing here is changed by this call.
/// </summary>
public record StorySyncReferencesResponse(Guid ContentProjectId, IReadOnlyList<string> SyncedLabels, IReadOnlyList<string> OutdatedLabels)
{
    public StorySyncReferencesResponse(Guid contentProjectId, IReadOnlyList<string> syncedLabels)
        : this(contentProjectId, syncedLabels, Array.Empty<string>())
    {
    }
}

/// <summary>
/// Wires a <see cref="StoryEpisode"/> into the existing ContentProject
/// pipeline. This is pure orchestration: it creates-or-reuses a
/// ContentProject from an episode's already-written script and hands off to
/// the unchanged Storyboard/Scene/Asset/TTS/Caption/Render/Publish pipeline
/// from there - no video-generation logic lives here. Kept separate from
/// <see cref="AiContentFactory.Application.Stories.Continuity.IStoryContinuityManager"/>
/// since this isn't an AI-orchestration operation (no agent calls).
/// </summary>
public interface IStoryVideoLinkService
{
    /// <summary>
    /// Null if the Story or Episode doesn't exist (or the episode belongs to
    /// a different Story). Throws <see cref="DomainException"/> if the
    /// episode has no script yet and no ContentProject is already linked. If
    /// the episode is linked to a ContentProject that no longer exists (e.g.
    /// its video was deleted), self-heals by clearing the stale link and
    /// creating a fresh ContentProject exactly as if this were a first-time
    /// "Create Video" call - never returns null just because the previously
    /// linked project is gone.
    /// </summary>
    Task<StoryVideoLinkResponse?> CreateOrOpenVideoAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-syncs Story-level approved Character/Location reference images onto
    /// an episode's already-linked ContentProject. Needed because
    /// <see cref="CreateOrOpenVideoAsync"/> only seeds references at creation
    /// time - an episode whose ContentProject was created before a Story-level
    /// image existed/was approved never gets it otherwise. Pure additive
    /// fill-the-gaps: never overwrites a slot that already has an Approved
    /// reference (regardless of how it got there); characters whose approved
    /// project reference no longer matches the Story's current approved image are
    /// only REPORTED (<see cref="StorySyncReferencesResponse.OutdatedLabels"/>).
    /// Null if the Story or Episode doesn't exist (or the episode belongs to a different Story).
    /// Throws <see cref="DomainException"/> if the episode has no linked
    /// ContentProject yet, or if it is linked to a ContentProject that no
    /// longer exists (the stale link is cleared as part of that failure so a
    /// subsequent <see cref="CreateOrOpenVideoAsync"/> call recreates one
    /// cleanly) - never silently writes AssetReference rows against a
    /// ContentProjectId that no longer exists.
    /// </summary>
    Task<StorySyncReferencesResponse?> SyncStoryReferencesAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default);
}

public class StoryVideoLinkService : IStoryVideoLinkService
{
    /// <summary>
    /// Matches the wizard/Advanced page's own default for a freshly-created
    /// ContentProject - see CreateContentProjectRequest.TargetDurationSeconds.
    /// </summary>
    internal const int DefaultTargetDurationSeconds = 60;

    private static readonly string[] SectionLabels =
    {
        "HOOK", "INTRODUCTION", "BODY", "ESCALATION", "PAYOFF", "CALL TO ACTION"
    };

    private readonly IStoryRepository _storyRepository;
    private readonly IContentProjectService _contentProjectService;
    private readonly IScriptService _scriptService;
    private readonly IAssetReferenceRepository _assetReferenceRepository;
    private readonly IPresetService _presetService;
    private readonly ILogger<StoryVideoLinkService> _logger;

    public StoryVideoLinkService(
        IStoryRepository storyRepository,
        IContentProjectService contentProjectService,
        IScriptService scriptService,
        IAssetReferenceRepository assetReferenceRepository,
        IPresetService presetService,
        ILogger<StoryVideoLinkService>? logger = null)
    {
        _storyRepository = storyRepository;
        _contentProjectService = contentProjectService;
        _scriptService = scriptService;
        _assetReferenceRepository = assetReferenceRepository;
        _presetService = presetService;
        _logger = logger ?? NullLogger<StoryVideoLinkService>.Instance;
    }

    public async Task<StoryVideoLinkResponse?> CreateOrOpenVideoAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        if (story is null)
        {
            return null;
        }

        var episode = await _storyRepository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId)
        {
            return null;
        }

        // "Open Video" - already linked, return unchanged. Never regenerate.
        if (episode.ContentProjectId is { } existingProjectId)
        {
            var existingProject = await _contentProjectService.GetByIdAsync(existingProjectId, cancellationToken);
            if (existingProject is not null)
            {
                return new StoryVideoLinkResponse(existingProject.Id, Created: false);
            }

            // Dangling link: the linked ContentProject is gone (its video was
            // deleted via ContentProjectsController.Delete, which does not
            // cascade back into StoryEpisode - see StoryEpisode.ContentProjectId).
            // Self-heal instead of returning null (which the controller reads as
            // "Story/Episode not found" - misleading, and leaves the user
            // permanently stuck): clear the stale link and fall through into the
            // exact same "Create Video" path below, as if creating for the first time.
            episode.ClearContentProjectLink();
            await _storyRepository.SaveChangesAsync(cancellationToken);
        }

        // "Create Video" path.
        if (string.IsNullOrWhiteSpace(episode.Script))
        {
            throw new DomainException("The episode must have a script before creating a video.");
        }

        var sections = ParseScriptSections(episode.Script);

        var project = await _contentProjectService.CreateAsync(
            new CreateContentProjectRequest(
                Title: $"{story.Title} - {episode.Title}",
                Topic: story.Premise,
                Niche: story.Niche,
                TargetDurationSeconds: DefaultTargetDurationSeconds,
                AspectRatio: story.AspectRatio,
                Language: story.Language),
            cancellationToken);

        // Mirrors ContentPipelineService's own sequence: persist the script,
        // then transition Draft -> ScriptReady, so the existing pipeline picks
        // up from exactly the state it expects after script generation.
        await _scriptService.UpsertAsync(
            project.Id,
            new UpsertScriptRequest(sections.Hook, sections.Introduction, sections.Body, sections.Escalation, sections.Payoff, sections.CallToAction),
            cancellationToken);

        await _contentProjectService.ChangeStatusAsync(
            project.Id,
            new ChangeContentProjectStatusRequest(ContentProjectStatus.ScriptReady),
            cancellationToken);

        var carriedForwardTypes = await CarryForwardVisualReferencesAsync(story, episode, project.Id, cancellationToken);
        _ = await SeedFromStoryReferencesAsync(story, project.Id, carriedForwardTypes, cancellationToken);
        await ApplyEpisodeStylePresetAsync(story, episode, project.Id, cancellationToken);

        episode.LinkContentProject(project.Id);
        await _storyRepository.SaveChangesAsync(cancellationToken);

        return new StoryVideoLinkResponse(project.Id, Created: true);
    }

    /// <summary>
    /// Re-sync entry point - see <see cref="IStoryVideoLinkService.SyncStoryReferencesAsync"/>.
    /// Unlike the creation path, this doesn't know/care about predecessor
    /// carry-forward: it just queries the LINKED project's own CURRENT
    /// Approved references and fills in whichever Character/Environment
    /// slot(s) are still missing from the Story's own canonical approved
    /// images.
    /// </summary>
    public async Task<StorySyncReferencesResponse?> SyncStoryReferencesAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        if (story is null)
        {
            return null;
        }

        var episode = await _storyRepository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId)
        {
            return null;
        }

        if (episode.ContentProjectId is not { } projectId || await _contentProjectService.GetByIdAsync(projectId, cancellationToken) is null)
        {
            if (episode.ContentProjectId is not null)
            {
                // Dangling link: the linked ContentProject is gone (see
                // CreateOrOpenVideoAsync for the same scenario). Clear it so a
                // subsequent "Create Video" call cleanly recreates one, and so we
                // don't fall through below and silently write AssetReference rows
                // against a ContentProjectId that no longer exists.
                episode.ClearContentProjectLink();
                await _storyRepository.SaveChangesAsync(cancellationToken);
            }

            throw new DomainException("This episode has no video yet - create one first before syncing reference images.");
        }

        var currentReferences = await _assetReferenceRepository.GetByProjectAsync(projectId, cancellationToken);
        var alreadyCoveredReferences = currentReferences
            .Where(r => r.Status == AssetReferenceStatus.Approved)
            .Select(r => (r.Type, r.Label))
            .ToHashSet();

        var outdatedLabels = FindOutdatedCharacterLabels(story, currentReferences);
        var syncedReferences = await SeedFromStoryReferencesAsync(story, projectId, alreadyCoveredReferences, cancellationToken);

        if (outdatedLabels.Count > 0)
        {
            _logger.LogInformation(
                "Story {StoryId} episode {EpisodeId}: project {ProjectId} still uses an older approved reference image for {OutdatedLabels}",
                storyId, episodeId, projectId, string.Join(", ", outdatedLabels));
        }

        return new StorySyncReferencesResponse(projectId, syncedReferences.Select(FormatSyncedLabel).ToList(), outdatedLabels);
    }

    /// <summary>
    /// Picks the visual style preset for a freshly-created episode
    /// ContentProject. Precedence: (1) the Story's own
    /// <see cref="Story.StylePresetId"/> when set (and still present in the
    /// preset catalog - a stale id is skipped rather than failing episode
    /// creation), else (2) the immediate predecessor episode's project style
    /// (<see cref="CarryForwardStylePresetAsync"/>), else nothing. Only ever
    /// applies to the NEW project: changing a Story's style never rewrites
    /// already-created episode projects.
    /// </summary>
    private async Task ApplyEpisodeStylePresetAsync(Story story, StoryEpisode episode, Guid newProjectId, CancellationToken cancellationToken)
    {
        if (story.StylePresetId is { } storyStyleId)
        {
            // Canonical catalog id (the lookup is case-insensitive/trimmed), so
            // the project never stores a differently-cased alias of the id.
            var storyStyle = PresetCatalog.FindStyle(storyStyleId);
            if (storyStyle is not null)
            {
                await _presetService.ApplyAsync(newProjectId, new ApplyPresetsRequest(null, storyStyle.Id, null, null), cancellationToken);
                return;
            }

            _logger.LogWarning(
                "Story {StoryId} has style preset '{StylePresetId}' which is no longer in the preset catalog; falling back to the predecessor episode's style for new project {ProjectId}",
                story.Id, storyStyleId, newProjectId);
        }

        await CarryForwardStylePresetAsync(episode, newProjectId, cancellationToken);
    }

    /// <summary>
    /// Carries the immediate predecessor episode's chosen visual style preset
    /// forward onto a freshly-created ContentProject, same one-hop
    /// predecessor lookup as <see cref="CarryForwardVisualReferencesAsync"/>.
    /// Used only when the Story itself has no style preset.
    /// Without this, every new episode of a Story silently fell back to
    /// <see cref="Generation.VideoPromptBuilder.DefaultStyleGuidance"/>
    /// ("Photorealistic documentary...") regardless of what style the rest of
    /// the series had already committed to - the exact bug that let an
    /// anthropomorphic-cat series drift back to a documentary look on every
    /// new episode. A no-op (not an error) when there's no predecessor, no
    /// linked project on it, or the predecessor never had a style preset set.
    /// </summary>
    private async Task CarryForwardStylePresetAsync(StoryEpisode episode, Guid newProjectId, CancellationToken cancellationToken)
    {
        if (episode.PreviousEpisodeId is not { } previousEpisodeId)
        {
            return;
        }

        var previousEpisode = await _storyRepository.GetEpisodeByIdAsync(previousEpisodeId, cancellationToken);
        if (previousEpisode?.ContentProjectId is not { } previousProjectId)
        {
            return;
        }

        var previousProject = await _contentProjectService.GetByIdAsync(previousProjectId, cancellationToken);
        if (previousProject?.StylePresetId is not { } stylePresetId)
        {
            return;
        }

        await _presetService.ApplyAsync(newProjectId, new ApplyPresetsRequest(null, stylePresetId, null, null), cancellationToken);
    }

    /// <summary>
    /// If the episode has a predecessor whose ContentProject already has
    /// Approved reference images, clone them onto the new project so the new
    /// episode starts with the same locked-in characters/environments. Copies
    /// EVERY Approved row regardless of (Type, Label) - a predecessor project
    /// may hold several labeled rows per type (e.g. "Milo" and "Mimi"), and
    /// each carries its own Label forward unchanged - EXCEPT Character rows
    /// whose label is a Story character that has its own Approved reference
    /// image: that Story-level image is the canonical asset and wins, so it is
    /// left for <see cref="SeedFromStoryReferencesAsync"/> to seed (otherwise
    /// replacing the Story-level image would never reach a later episode).
    /// Environment rows and Character rows with no Story-level approved image
    /// keep the predecessor copy. A no-op (not an error)
    /// when there's no predecessor, no linked project on it, or no Approved
    /// references yet. Returns the set of (Type, Label) pairs that were
    /// actually carried forward, so <see cref="SeedFromStoryReferencesAsync"/>
    /// knows which named references (if any) are still uncovered.
    /// </summary>
    private async Task<IReadOnlySet<(AssetReferenceType Type, string? Label)>> CarryForwardVisualReferencesAsync(Story story, StoryEpisode episode, Guid newProjectId, CancellationToken cancellationToken)
    {
        if (episode.PreviousEpisodeId is not { } previousEpisodeId)
        {
            return new HashSet<(AssetReferenceType, string?)>();
        }

        var previousEpisode = await _storyRepository.GetEpisodeByIdAsync(previousEpisodeId, cancellationToken);
        if (previousEpisode?.ContentProjectId is not { } previousProjectId)
        {
            return new HashSet<(AssetReferenceType, string?)>();
        }

        var previousReferences = await _assetReferenceRepository.GetByProjectAsync(previousProjectId, cancellationToken);
        // Names match like ReferenceMatcher matches labels: trimmed, case-insensitive.
        var canonicalCharacterNames = story.Characters
            .Where(c => c.ReferenceImageStatus == AssetReferenceStatus.Approved && c.ReferenceImagePath is not null)
            .Select(c => c.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var approved = previousReferences
            .Where(r => r.Status == AssetReferenceStatus.Approved && r.ImagePath is not null)
            .Where(r => !(r.Type == AssetReferenceType.Character && r.Label is not null && canonicalCharacterNames.Contains(r.Label.Trim())))
            .ToList();
        if (approved.Count == 0)
        {
            return new HashSet<(AssetReferenceType, string?)>();
        }

        var copies = approved
            .Select(reference =>
            {
                var copy = AssetReference.CreateGenerated(newProjectId, reference.Type, reference.ImagePath!, reference.Prompt, reference.Provider, reference.Label);
                copy.Approve();
                return copy;
            })
            .ToList();

        await _assetReferenceRepository.AddRangeAsync(copies, cancellationToken);
        await _assetReferenceRepository.SaveChangesAsync(cancellationToken);

        return copies.Select(c => (c.Type, c.Label)).ToHashSet();
    }

    /// <summary>
    /// Seeds ONE <see cref="AssetReference"/> row per approved
    /// <see cref="StoryCharacter"/>/<see cref="StoryLocation"/>, each labeled
    /// with its own Name, for whichever named reference the predecessor
    /// copy-forward above left uncovered. For CHARACTERS the Story-level
    /// approved image is the canonical asset (carry-forward deliberately skips
    /// those rows), so it is what a new episode gets; predecessor copies only
    /// remain for characters with no Story-level approved image. For
    /// LOCATIONS the predecessor's copy still takes precedence and the
    /// Story-level image is just the fallback. A no-op (not an error) for any character/location that
    /// already has coverage or has no approved reference image at all.
    /// Returns the set of (Type, Label) pairs actually seeded by this call
    /// (empty when nothing needed seeding), so callers that care - e.g.
    /// <see cref="SyncStoryReferencesAsync"/> - can report what changed; the
    /// creation path ignores the return value.
    /// </summary>
    private async Task<IReadOnlySet<(AssetReferenceType Type, string? Label)>> SeedFromStoryReferencesAsync(
        Story story,
        Guid newProjectId,
        IReadOnlySet<(AssetReferenceType Type, string? Label)> alreadyCoveredReferences,
        CancellationToken cancellationToken)
    {
        var seeds = new List<AssetReference>();

        foreach (var character in story.Characters)
        {
            if (character.ReferenceImageStatus != AssetReferenceStatus.Approved || character.ReferenceImagePath is null)
            {
                continue;
            }

            if (IsCharacterCovered(alreadyCoveredReferences, character.Name))
            {
                continue;
            }

            seeds.Add(CreateApprovedSeed(newProjectId, AssetReferenceType.Character, character.ReferenceImagePath!, character.ReferenceImagePrompt, character.ReferenceImageProvider, character.Name));
        }

        foreach (var location in story.Locations)
        {
            if (location.ReferenceImageStatus != AssetReferenceStatus.Approved || location.ReferenceImagePath is null)
            {
                continue;
            }

            if (alreadyCoveredReferences.Contains((AssetReferenceType.Environment, location.Name)))
            {
                continue;
            }

            seeds.Add(CreateApprovedSeed(newProjectId, AssetReferenceType.Environment, location.ReferenceImagePath!, location.ReferenceImagePrompt, location.ReferenceImageProvider, location.Name));
        }

        if (seeds.Count == 0)
        {
            return new HashSet<(AssetReferenceType, string?)>();
        }

        await _assetReferenceRepository.AddRangeAsync(seeds, cancellationToken);
        await _assetReferenceRepository.SaveChangesAsync(cancellationToken);

        return seeds.Select(s => (s.Type, s.Label)).ToHashSet();
    }

    /// <summary>
    /// Names of Story characters whose current approved Story-level image is
    /// not the image any of this project's Approved rows for that name uses.
    /// A character with no approved project row at all is NOT outdated (sync
    /// seeds it instead), and a character with no approved Story image has
    /// nothing to compare against.
    /// </summary>
    private static IReadOnlyList<string> FindOutdatedCharacterLabels(Story story, IReadOnlyList<AssetReference> projectReferences)
    {
        var outdated = new List<string>();

        foreach (var character in story.Characters)
        {
            if (character.ReferenceImageStatus != AssetReferenceStatus.Approved || character.ReferenceImagePath is null)
            {
                continue;
            }

            var approvedForName = projectReferences
                .Where(r => r.Type == AssetReferenceType.Character && r.Status == AssetReferenceStatus.Approved && LabelMatchesName(r.Label, character.Name))
                .ToList();

            if (approvedForName.Count > 0 && approvedForName.All(r => !string.Equals(r.ImagePath, character.ReferenceImagePath, StringComparison.Ordinal)))
            {
                outdated.Add(character.Name);
            }
        }

        return outdated;
    }

    /// <summary>Character names and reference labels are compared trimmed and case-insensitively, consistent with <c>ReferenceMatcher</c>.</summary>
    private static bool LabelMatchesName(string? label, string name) =>
        label is not null && string.Equals(label.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsCharacterCovered(IReadOnlySet<(AssetReferenceType Type, string? Label)> covered, string characterName) =>
        covered.Any(c => c.Type == AssetReferenceType.Character && LabelMatchesName(c.Label, characterName));

    private static AssetReference CreateApprovedSeed(Guid contentProjectId, AssetReferenceType type, string imagePath, string? prompt, string? provider, string? label = null)
    {
        var seed = AssetReference.CreateGenerated(contentProjectId, type, imagePath, prompt, provider, label);
        seed.Approve();
        return seed;
    }

    /// <summary>Formats a (Type, Label) pair for <see cref="StorySyncReferencesResponse.SyncedLabels"/> - see that property's remarks.</summary>
    private static string FormatSyncedLabel((AssetReferenceType Type, string? Label) reference) =>
        reference.Label is null ? reference.Type.ToString() : $"{reference.Type}: {reference.Label}";

    /// <summary>
    /// Parses the six labeled sections <see cref="AiContentFactory.Application.Stories.Continuity.StoryContinuityManager"/>'s
    /// ComposeScript produces back apart. Falls back to putting the WHOLE
    /// text into Body (everything else blank) when the expected labels
    /// aren't found in order - a manually-edited script (PUT .../script) may
    /// not follow this shape at all, and should still be usable rather than
    /// throwing.
    /// </summary>
    internal static ScriptSections ParseScriptSections(string script)
    {
        var positions = new List<(int LabelStart, int ContentStart)>();

        foreach (var label in SectionLabels)
        {
            var match = Regex.Match(script, $@"(?m)^{Regex.Escape(label)}:[ \t]*\r?\n");
            if (!match.Success)
            {
                return FallbackToBody(script);
            }

            positions.Add((match.Index, match.Index + match.Length));
        }

        for (var i = 1; i < positions.Count; i++)
        {
            if (positions[i].LabelStart <= positions[i - 1].LabelStart)
            {
                // Out-of-order/duplicated labels - not the expected shape.
                return FallbackToBody(script);
            }
        }

        var values = new string[SectionLabels.Length];
        for (var i = 0; i < positions.Count; i++)
        {
            var contentStart = positions[i].ContentStart;
            var contentEnd = i + 1 < positions.Count ? positions[i + 1].LabelStart : script.Length;
            values[i] = script[contentStart..contentEnd].Trim();
        }

        return new ScriptSections(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    private static ScriptSections FallbackToBody(string script) =>
        new(string.Empty, string.Empty, script.Trim(), string.Empty, string.Empty, string.Empty);
}

/// <summary>The six sections of a Story episode's script, parsed back out of its plain-text <see cref="StoryEpisode.Script"/> field.</summary>
internal readonly record struct ScriptSections(
    string Hook, string Introduction, string Body, string Escalation, string Payoff, string CallToAction);
