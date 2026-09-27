using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

/// <param name="StylePresetId">
/// Optional series-level visual style (an id from the preset catalog, e.g.
/// "pixar-3d"). Null/blank = no series style chosen. An id that is not in the
/// catalog is rejected with a <see cref="AiContentFactory.Domain.Exceptions.DomainException"/>
/// (a 400 validation problem at the API layer) - same rule as applying a
/// style preset to a ContentProject.
/// </param>
public record CreateStoryRequest(
    string Title,
    string? Premise,
    string? Niche,
    string? Language,
    string? AspectRatio,
    string? StylePresetId = null);

/// <param name="StylePresetId">
/// Optional. Unlike Premise/Niche (plain PUT semantics: null clears), a null
/// or omitted StylePresetId LEAVES THE CURRENT STYLE UNCHANGED - so a client
/// that predates this field can never wipe a series' style by re-saving the
/// other details. A blank/whitespace string explicitly CLEARS the style; a
/// non-blank id must exist in the preset catalog (else DomainException/400).
/// Changing it only affects future generation - already-created episode
/// ContentProjects keep their own style.
/// </param>
public record UpdateStoryRequest(
    string Title,
    string? Premise,
    string? Niche,
    string? StylePresetId = null);

/// <summary>
/// Episode creation is intentionally minimal - Script/Summary are not
/// accepted here (a freshly-created episode is always Draft with neither
/// set, per <see cref="StoryEpisode.Create"/>'s own signature). No AI
/// generation is wired up in this pass.
/// </summary>
public record CreateStoryEpisodeRequest(
    int EpisodeNumber,
    string Title,
    Guid? PreviousEpisodeId);

/// <summary>
/// Plain CRUD script edit (PUT .../script) - no AI call, distinct from
/// POST .../script (<see cref="AiContentFactory.Application.Stories.Continuity.IStoryContinuityManager.WriteScriptAsync"/>)
/// which always regenerates via the writer agent. Lets a human submit a
/// manually-edited script directly via <see cref="StoryEpisode.SetScript"/>.
/// </summary>
public record UpdateStoryEpisodeScriptRequest(string Script);

/// <param name="Kind">Optional canonical classification; null = <see cref="CharacterKind.Unspecified"/>. Serialised as a string like <see cref="CharacterBehaviorProfile"/>.</param>
/// <param name="Species">Optional canonical species/breed (max <see cref="StoryCharacter.SpeciesMaxLength"/> characters).</param>
/// <param name="ClothingAndAccessories">Optional canonical clothing/accessories (max <see cref="StoryCharacter.ClothingAndAccessoriesMaxLength"/> characters).</param>
/// <param name="DistinctiveFeatures">Optional canonical distinctive features (max <see cref="StoryCharacter.DistinctiveFeaturesMaxLength"/> characters).</param>
public record CreateStoryCharacterRequest(
    string Name,
    string? Description,
    string? VisualDescription,
    CharacterBehaviorProfile BehaviorProfile = CharacterBehaviorProfile.None,
    CharacterKind? Kind = null,
    string? Species = null,
    string? ClothingAndAccessories = null,
    string? DistinctiveFeatures = null);

/// <summary>
/// Plain CRUD character edit (PUT .../characters/{characterId}) - no AI call.
/// Lets a human correct a character's name/description/appearance, or opt an
/// already-created character into (or out of) a <see cref="CharacterBehaviorProfile"/>
/// (e.g. Milo/Mimi were created before the profile existed, or the checkbox
/// was missed at creation time) without needing to recreate the Story.
/// </summary>
/// <param name="Kind">Optional. null/omitted leaves the current kind unchanged.</param>
/// <param name="Species">Optional. null/omitted leaves the value unchanged; a blank string clears it.</param>
/// <param name="ClothingAndAccessories">Optional. null/omitted leaves the value unchanged; a blank string clears it.</param>
/// <param name="DistinctiveFeatures">Optional. null/omitted leaves the value unchanged; a blank string clears it.</param>
public record UpdateStoryCharacterRequest(
    string Name,
    string? Description,
    string? VisualDescription,
    CharacterBehaviorProfile BehaviorProfile = CharacterBehaviorProfile.None,
    CharacterKind? Kind = null,
    string? Species = null,
    string? ClothingAndAccessories = null,
    string? DistinctiveFeatures = null);

public record CreateStoryLocationRequest(
    string Name,
    string? Description,
    string? VisualDescription);

/// <param name="MissingReferenceFields">Advisory ids (appearance/species/clothingAndAccessories/distinctiveFeatures) of canonical inputs the reference prompt would lack. Never blocks anything.</param>
/// <param name="CanGenerateReference">True when generating a reference image is allowed: the character has an appearance (VisualDescription), or a species while its kind is not Human.</param>
/// <param name="PendingVersion">Short stable identity of the pending candidate image (null when there is none) - see <see cref="ReferencePendingVersion"/>.</param>
public record StoryCharacterResponse(
    Guid Id,
    string Name,
    string? Description,
    string? VisualDescription,
    string ReferenceImageStatus,
    bool HasReferenceImage,
    string BehaviorProfile,
    string Kind,
    string? Species,
    string? ClothingAndAccessories,
    string? DistinctiveFeatures,
    bool HasPendingReferenceImage,
    string ReferenceImageSource,
    string? PendingReferenceImageSource,
    IReadOnlyList<string> MissingReferenceFields,
    bool CanGenerateReference,
    string? PendingVersion = null)
{
    public static StoryCharacterResponse FromDomain(StoryCharacter character)
    {
        // Style plays no part in either value, so no Story/preset is needed here.
        var spec = CharacterReferenceSpec.FromCharacter(character, style: null);

        return new(
            character.Id,
            character.Name,
            character.Description,
            character.VisualDescription,
            character.ReferenceImageStatus.ToString(),
            character.ReferenceImagePath is not null,
            character.BehaviorProfile.ToString(),
            character.Kind.ToString(),
            character.Species,
            character.ClothingAndAccessories,
            character.DistinctiveFeatures,
            character.HasPendingReferenceImage,
            character.ReferenceImageSource.ToString(),
            character.PendingReferenceImageSource?.ToString(),
            CharacterReferencePromptComposer.GetMissingFields(spec),
            spec.HasEnoughToGenerate,
            ReferencePendingVersion.Of(character));
    }
}

public record StoryLocationResponse(
    Guid Id,
    string Name,
    string? Description,
    string? VisualDescription,
    string ReferenceImageStatus,
    bool HasReferenceImage)
{
    public static StoryLocationResponse FromDomain(StoryLocation location) => new(
        location.Id,
        location.Name,
        location.Description,
        location.VisualDescription,
        location.ReferenceImageStatus.ToString(),
        location.ReferenceImagePath is not null);
}

/// <summary>
/// Lightweight episode projection - no Script/Summary - used both for the
/// nested list on <see cref="StoryResponse"/> and for the dedicated
/// GET /stories/{id}/episodes list. Full script/summary/state-snapshot only
/// come back from the single-episode detail endpoint (<see cref="StoryEpisodeResponse"/>).
/// </summary>
public record StoryEpisodeSummaryResponse(
    Guid Id,
    Guid StoryId,
    int EpisodeNumber,
    string Title,
    string Status,
    Guid? ContentProjectId)
{
    public static StoryEpisodeSummaryResponse FromDomain(StoryEpisode episode) => new(
        episode.Id,
        episode.StoryId,
        episode.EpisodeNumber,
        episode.Title,
        episode.Status.ToString(),
        episode.ContentProjectId);
}

public record StoryStateSnapshotResponse(
    string? CurrentLocation,
    string? CurrentObjective,
    string? CharacterStates,
    IReadOnlyList<string> ImportantEvents,
    IReadOnlyList<string> OpenStoryThreads,
    IReadOnlyList<string> UnresolvedConflicts,
    IReadOnlyList<string> KnownFacts,
    string? NextPlannedDestination,
    string? Notes)
{
    public static StoryStateSnapshotResponse FromDomain(StoryStateSnapshot snapshot) => new(
        snapshot.CurrentLocation,
        snapshot.CurrentObjective,
        snapshot.CharacterStates,
        snapshot.ImportantEvents,
        snapshot.OpenStoryThreads,
        snapshot.UnresolvedConflicts,
        snapshot.KnownFacts,
        snapshot.NextPlannedDestination,
        snapshot.Notes);
}

public record StoryEpisodeResponse(
    Guid Id,
    Guid StoryId,
    int EpisodeNumber,
    string Title,
    string? Script,
    string? Summary,
    Guid? PreviousEpisodeId,
    string Status,
    StoryStateSnapshotResponse? StoryStateSnapshot,
    Guid? ContentProjectId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static StoryEpisodeResponse FromDomain(StoryEpisode episode) => new(
        episode.Id,
        episode.StoryId,
        episode.EpisodeNumber,
        episode.Title,
        episode.Script,
        episode.Summary,
        episode.PreviousEpisodeId,
        episode.Status.ToString(),
        episode.StoryStateSnapshot is null ? null : StoryStateSnapshotResponse.FromDomain(episode.StoryStateSnapshot),
        episode.ContentProjectId,
        episode.CreatedAt,
        episode.UpdatedAt);
}

public record StoryStateResponse(
    Guid StoryId,
    string? CurrentLocation,
    string? CurrentObjective,
    string? CharacterStates,
    IReadOnlyList<string> ImportantEvents,
    IReadOnlyList<string> OpenStoryThreads,
    IReadOnlyList<string> UnresolvedConflicts,
    IReadOnlyList<string> KnownFacts,
    string? NextPlannedDestination,
    string? Notes,
    DateTimeOffset UpdatedAt)
{
    public static StoryStateResponse FromDomain(StoryState state) => new(
        state.StoryId,
        state.CurrentLocation,
        state.CurrentObjective,
        state.CharacterStates,
        state.ImportantEvents,
        state.OpenStoryThreads,
        state.UnresolvedConflicts,
        state.KnownFacts,
        state.NextPlannedDestination,
        state.Notes,
        state.UpdatedAt);
}

/// <summary>
/// Story detail/list projection. Nested Characters/Locations are full
/// (small, bounded "bible" lists); Episodes is deliberately the lightweight
/// <see cref="StoryEpisodeSummaryResponse"/> shape (no Script/Summary text)
/// to keep this payload light - see the type's own remarks.
/// </summary>
public record StoryResponse(
    Guid Id,
    string Title,
    string? Premise,
    string? Niche,
    string Language,
    string AspectRatio,
    string? StylePresetId,
    IReadOnlyList<StoryCharacterResponse> Characters,
    IReadOnlyList<StoryLocationResponse> Locations,
    IReadOnlyList<StoryEpisodeSummaryResponse> Episodes,
    int EpisodeCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static StoryResponse FromDomain(Story story) => new(
        story.Id,
        story.Title,
        story.Premise,
        story.Niche,
        story.Language,
        story.AspectRatio,
        story.StylePresetId,
        story.Characters.Select(StoryCharacterResponse.FromDomain).ToList(),
        story.Locations.Select(StoryLocationResponse.FromDomain).ToList(),
        story.Episodes
            .OrderBy(e => e.EpisodeNumber)
            .Select(StoryEpisodeSummaryResponse.FromDomain)
            .ToList(),
        story.Episodes.Count,
        story.CreatedAt,
        story.UpdatedAt);
}
