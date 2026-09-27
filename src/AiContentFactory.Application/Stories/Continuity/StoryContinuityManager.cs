using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories.Continuity;

public class StoryContinuityManager : IStoryContinuityManager
{
    /// <summary>Short mechanical fallback used only when the writer agent doesn't provide its own Summary.</summary>
    private const int FallbackSummaryMaxChars = 400;

    private readonly IStoryRepository _repository;
    private readonly IStoryPlannerAgent _storyPlannerAgent;
    private readonly IEpisodePlannerAgent _episodePlannerAgent;
    private readonly IStoryScriptWriterAgent _scriptWriterAgent;
    private readonly IStoryContinuityAnalysisAgent _continuityAnalysisAgent;
    private readonly IContinuityValidatorAgent _continuityValidatorAgent;

    public StoryContinuityManager(
        IStoryRepository repository,
        IStoryPlannerAgent storyPlannerAgent,
        IEpisodePlannerAgent episodePlannerAgent,
        IStoryScriptWriterAgent scriptWriterAgent,
        IStoryContinuityAnalysisAgent continuityAnalysisAgent,
        IContinuityValidatorAgent continuityValidatorAgent)
    {
        _repository = repository;
        _storyPlannerAgent = storyPlannerAgent;
        _episodePlannerAgent = episodePlannerAgent;
        _scriptWriterAgent = scriptWriterAgent;
        _continuityAnalysisAgent = continuityAnalysisAgent;
        _continuityValidatorAgent = continuityValidatorAgent;
    }

    public async Task<StoryBibleResponse?> PlanStoryAsync(Guid storyId, PlanStoryBibleRequest request, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        var output = await _storyPlannerAgent.GenerateAsync(new StoryPlannerAgentInput(
            story.Title,
            request.Theme,
            request.Genre,
            request.Tone,
            request.TargetAudience,
            BuildCharacterLines(story, request),
            BuildLocationLines(story, request),
            request.StoryRulesConstraints,
            request.DesiredEpisodeCount), cancellationToken);

        var bible = StoryBible.Create(
            output.Premise,
            output.WorldRules,
            output.CharacterDefinitions,
            output.CharacterRelationships,
            output.VisualConsistencyRules,
            output.Tone,
            output.RecurringElements,
            output.StoryConstraints,
            output.StoryArc);

        story.SetBible(bible);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryBibleResponse.FromDomain(bible);
    }

    public async Task<StoryBibleResponse?> GetBibleAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        return story.Bible is null ? null : StoryBibleResponse.FromDomain(story.Bible);
    }

    public async Task<StoryBibleResponse?> UpdateBibleAsync(Guid storyId, UpdateStoryBibleRequest request, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        var bible = StoryBible.Create(
            request.Premise,
            request.WorldRules,
            request.CharacterDefinitions,
            request.CharacterRelationships,
            request.VisualConsistencyRules,
            request.Tone,
            request.RecurringElements,
            request.StoryConstraints,
            request.StoryArc);

        story.SetBible(bible);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryBibleResponse.FromDomain(bible);
    }

    public async Task<StoryEpisodeOutlineResponse?> PlanEpisodeAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId) return null;

        if (story.Bible is null)
        {
            throw new DomainException("A Story Bible must be generated (POST /stories/{id}/bible) before planning an episode.");
        }

        // Single hop via PreviousEpisodeId only - never walk the whole chain,
        // never load another episode's full Script.
        var previousEpisode = episode.PreviousEpisodeId is { } previousId
            ? await _repository.GetEpisodeByIdAsync(previousId, cancellationToken)
            : null;

        var state = await ResolveBaselineStateAsync(storyId, previousEpisode, cancellationToken);

        var output = await _episodePlannerAgent.GenerateAsync(new EpisodePlannerAgentInput(
            story.Title,
            episode.EpisodeNumber,
            ContinuityPromptContext.BuildBibleSummary(story.Bible),
            story.Bible.StoryArc,
            state?.CurrentLocation,
            state?.CurrentObjective,
            state?.OpenStoryThreads,
            ContinuityPromptContext.Truncate(previousEpisode?.Summary),
            ContinuityPromptContext.DerivePreviousEnding(previousEpisode?.Summary)),
            cancellationToken);

        var outline = StoryEpisodeOutline.Create(
            output.Title,
            output.Objective,
            output.Setup,
            output.MajorBeats,
            output.Conflict,
            output.Escalation,
            output.Resolution,
            output.Cliffhanger,
            output.ContinuityRequirements,
            output.ScenesRequired);

        episode.SetOutline(outline);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryEpisodeOutlineResponse.FromDomain(episode.Id, outline);
    }

    public async Task<StoryEpisodeOutlineResponse?> GetOutlineAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId) return null;

        return episode.Outline is null ? null : StoryEpisodeOutlineResponse.FromDomain(episode.Id, episode.Outline);
    }

    public async Task<StoryEpisodeResponse?> WriteScriptAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId) return null;

        if (episode.Outline is null)
        {
            throw new DomainException("An Episode Outline must be generated (POST .../outline) before writing a script.");
        }

        var previousEpisode = episode.PreviousEpisodeId is { } previousId
            ? await _repository.GetEpisodeByIdAsync(previousId, cancellationToken)
            : null;

        var state = await ResolveBaselineStateAsync(storyId, previousEpisode, cancellationToken);

        var output = await _scriptWriterAgent.GenerateAsync(new StoryScriptWriterAgentInput(
            story.Title,
            ContinuityPromptContext.BuildBibleSummary(story.Bible),
            ContinuityPromptContext.BuildVisualConsistencyRules(story.Bible),
            story.Bible?.CharacterDefinitions,
            state?.CurrentLocation,
            state?.CurrentObjective,
            state?.CharacterStates,
            ContinuityPromptContext.Truncate(previousEpisode?.Summary),
            ContinuityPromptContext.BuildOutlineSummary(episode.Outline),
            story.Language),
            cancellationToken);

        episode.SetScript(ComposeScript(output));

        var summary = string.IsNullOrWhiteSpace(output.Summary)
            ? BuildFallbackSummary(output)
            : output.Summary.Trim();
        episode.SetSummary(summary);

        await _repository.SaveChangesAsync(cancellationToken);

        return StoryEpisodeResponse.FromDomain(episode);
    }

    public async Task<ContinuityValidationResponse?> ValidateEpisodeAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId) return null;

        var content = ResolveEpisodeContent(episode);

        // Single hop via PreviousEpisodeId only, same as PlanEpisodeAsync/WriteScriptAsync -
        // never walk the whole chain, never load another episode's full Script.
        var previousEpisode = episode.PreviousEpisodeId is { } previousId
            ? await _repository.GetEpisodeByIdAsync(previousId, cancellationToken)
            : null;

        var state = await ResolveBaselineStateAsync(storyId, previousEpisode, cancellationToken);

        var output = await _continuityValidatorAgent.GenerateAsync(new ContinuityValidatorAgentInput(
            story.Title,
            ContinuityPromptContext.BuildBibleSummary(story.Bible),
            ContinuityPromptContext.Truncate(content),
            state?.CurrentLocation,
            state?.CurrentObjective,
            state?.CharacterStates,
            state?.OpenStoryThreads,
            state?.UnresolvedConflicts,
            state?.KnownFacts),
            cancellationToken);

        return ContinuityValidationResponse.FromOutput(output);
    }

    public async Task<FinalizeEpisodeResponse?> FinalizeEpisodeAsync(Guid storyId, Guid episodeId, CancellationToken cancellationToken = default)
    {
        var story = await _repository.GetByIdAsync(storyId, cancellationToken);
        if (story is null) return null;

        var episode = await _repository.GetEpisodeByIdAsync(episodeId, cancellationToken);
        if (episode is null || episode.StoryId != storyId) return null;

        if (string.IsNullOrWhiteSpace(episode.Script))
        {
            throw new DomainException("The episode must have a script (POST .../script) before it can be finalized.");
        }

        // Guard against finalizing out of order: finalizing advances the live
        // StoryState forward from "wherever it currently is", so finalizing an
        // episode whose predecessor hasn't been completed yet would corrupt
        // the live state (it wouldn't reflect the predecessor's ending).
        if (episode.PreviousEpisodeId is { } previousEpisodeId)
        {
            var previousEpisode = await _repository.GetEpisodeByIdAsync(previousEpisodeId, cancellationToken);
            if (previousEpisode is null || previousEpisode.Status != StoryEpisodeStatus.Completed)
            {
                throw new DomainException("The previous episode must be finalized (Completed) before this episode can be finalized.");
            }
        }

        // Gate: run continuity validation first. If invalid, stop here -
        // never touch StoryState, never complete the episode, so the caller
        // can regenerate instead of the canonical state being silently
        // overwritten by an inconsistent episode.
        var validation = await ValidateEpisodeAsync(storyId, episodeId, cancellationToken);
        if (validation is null) return null; // defensive: story/episode vanished mid-call

        if (!validation.Valid)
        {
            return new FinalizeEpisodeResponse(false, validation.Score, validation.Warnings, validation.CriticalIssues, Episode: null);
        }

        var state = await _repository.GetStateByStoryIdAsync(storyId, cancellationToken);
        if (state is null)
        {
            state = StoryState.Create(storyId);
            await _repository.AddStateAsync(state, cancellationToken);
        }

        var analysis = await _continuityAnalysisAgent.GenerateAsync(new StoryContinuityAnalysisAgentInput(
            story.Title,
            ContinuityPromptContext.BuildBibleSummary(story.Bible),
            ContinuityPromptContext.Truncate(episode.Script),
            state.CurrentLocation,
            state.CurrentObjective,
            state.CharacterStates,
            state.ImportantEvents,
            state.OpenStoryThreads,
            state.UnresolvedConflicts,
            state.KnownFacts,
            state.NextPlannedDestination,
            state.Notes),
            cancellationToken);

        state.Update(
            analysis.CurrentLocation,
            analysis.CurrentObjective,
            analysis.CharacterStates,
            analysis.ImportantEvents,
            analysis.OpenStoryThreads,
            analysis.UnresolvedConflicts,
            analysis.KnownFacts,
            analysis.NextPlannedDestination,
            analysis.Notes);

        episode.Complete(StoryStateSnapshot.FromState(state));

        await _repository.SaveChangesAsync(cancellationToken);

        return new FinalizeEpisodeResponse(true, validation.Score, validation.Warnings, validation.CriticalIssues, StoryEpisodeResponse.FromDomain(episode));
    }

    /// <summary>
    /// The 9 fields every episode-generation/validation call needs, regardless
    /// of whether they come from the story's live StoryState row or from a
    /// completed episode's frozen StoryStateSnapshot.
    /// </summary>
    private sealed record EffectiveState(
        string? CurrentLocation, string? CurrentObjective, string? CharacterStates,
        IReadOnlyList<string> ImportantEvents, IReadOnlyList<string> OpenStoryThreads,
        IReadOnlyList<string> UnresolvedConflicts, IReadOnlyList<string> KnownFacts,
        string? NextPlannedDestination, string? Notes)
    {
        /// <summary>The correct baseline for a genesis episode (no predecessor at all): nothing precedes it, so every field is empty regardless of how far the live StoryState has since advanced.</summary>
        public static readonly EffectiveState Empty = new(
            null, null, null,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            null, null);

        public static EffectiveState? FromLiveState(StoryState? state) => state is null ? null : new(
            state.CurrentLocation, state.CurrentObjective, state.CharacterStates,
            state.ImportantEvents, state.OpenStoryThreads, state.UnresolvedConflicts, state.KnownFacts,
            state.NextPlannedDestination, state.Notes);

        public static EffectiveState FromSnapshot(StoryStateSnapshot snapshot) => new(
            snapshot.CurrentLocation, snapshot.CurrentObjective, snapshot.CharacterStates,
            snapshot.ImportantEvents, snapshot.OpenStoryThreads, snapshot.UnresolvedConflicts, snapshot.KnownFacts,
            snapshot.NextPlannedDestination, snapshot.Notes);
    }

    /// <summary>
    /// Resolves "the canonical state immediately before this episode" - the
    /// previous episode's frozen snapshot when available (correct regardless of
    /// how far the live StoryState has since advanced). When there is no
    /// predecessor at all (<paramref name="previousEpisode"/> is null - Episode 1,
    /// or an orphan first episode), the baseline is always empty, never the live
    /// StoryState row, since that row may already reflect a later episode's
    /// progress (e.g. Episode 1 being regenerated/revalidated after Episode 2+
    /// has advanced the story). Falling back to the live StoryState row remains
    /// correct only when a predecessor exists but hasn't been finalized yet
    /// (best available approximation). <paramref name="previousEpisode"/> is the
    /// caller's already-resolved single hop via PreviousEpisodeId (null if there
    /// is none) - callers must not fetch it twice.
    /// </summary>
    private async Task<EffectiveState?> ResolveBaselineStateAsync(Guid storyId, StoryEpisode? previousEpisode, CancellationToken cancellationToken)
    {
        if (previousEpisode is null)
        {
            return EffectiveState.Empty;
        }

        if (previousEpisode.StoryStateSnapshot is { } snapshot)
        {
            return EffectiveState.FromSnapshot(snapshot);
        }

        return EffectiveState.FromLiveState(await _repository.GetStateByStoryIdAsync(storyId, cancellationToken));
    }

    /// <summary>Prefers the finalized script; falls back to the outline when no script exists yet (e.g. validating before writing).</summary>
    private static string ResolveEpisodeContent(StoryEpisode episode)
    {
        if (!string.IsNullOrWhiteSpace(episode.Script)) return episode.Script;
        if (episode.Outline is not null) return ContinuityPromptContext.BuildOutlineSummary(episode.Outline);

        throw new DomainException("The episode has neither a script nor an outline to validate yet.");
    }

    /// <summary>
    /// Deterministic join format for StoryEpisode.Script (a single text
    /// field): each of the writer's six sections, labelled and separated by
    /// a blank line, in Hook/Introduction/Body/Escalation/Payoff/CallToAction
    /// order - matches AiContentFactory.Application.Agents.ScriptAgentOutput's
    /// field order so a future integration step can parse it back out.
    /// </summary>
    private static string ComposeScript(StoryScriptWriterOutput output) => string.Join("\n\n", new[]
    {
        $"HOOK:\n{output.Hook.Trim()}",
        $"INTRODUCTION:\n{output.Introduction.Trim()}",
        $"BODY:\n{output.Body.Trim()}",
        $"ESCALATION:\n{output.Escalation.Trim()}",
        $"PAYOFF:\n{output.Payoff.Trim()}",
        $"CALL TO ACTION:\n{output.CallToAction.Trim()}"
    });

    private static string BuildFallbackSummary(StoryScriptWriterOutput output) =>
        ContinuityPromptContext.Truncate($"{output.Hook} {output.Payoff}", FallbackSummaryMaxChars);

    /// <summary>
    /// Builds the "Main characters" lines sent to <see cref="IStoryPlannerAgent"/>.
    ///
    /// Bug fix: PlanStoryAsync used to pass only <see cref="PlanStoryBibleRequest.MainCharacters"/>
    /// (free-text strings from the POST /bible request body) straight through,
    /// completely ignoring the persisted <see cref="StoryCharacter"/> entities
    /// created via POST /stories/{id}/characters - so a character's
    /// VisualDescription never reached the planner, which then had nothing to
    /// stay consistent with and simply invented an appearance.
    ///
    /// Precedence: the persisted <see cref="Story.Characters"/> entities are
    /// authoritative when present, since they are the only source that
    /// carries VisualDescription. When present, request.MainCharacters is
    /// appended as additional free-text context rather than silently
    /// dropped - a caller may still supply extra notes alongside real
    /// character records. When no persisted characters exist yet (e.g. a
    /// Bible generated before any character record was created),
    /// request.MainCharacters remains the sole source, preserving that
    /// existing flexible path.
    /// </summary>
    private static IReadOnlyList<string> BuildCharacterLines(Story story, PlanStoryBibleRequest request)
    {
        var lines = story.Characters.Select(FormatCharacterLine).ToList();

        if (request.MainCharacters is { Count: > 0 })
        {
            lines.AddRange(request.MainCharacters);
        }

        return lines;
    }

    /// <summary>Same precedence rule as <see cref="BuildCharacterLines"/>, for locations.</summary>
    private static IReadOnlyList<string> BuildLocationLines(Story story, PlanStoryBibleRequest request)
    {
        var lines = story.Locations.Select(FormatLocationLine).ToList();

        if (request.Locations is { Count: > 0 })
        {
            lines.AddRange(request.Locations);
        }

        return lines;
    }

    /// <summary>"{Name}: {Description} (Appearance: {VisualDescription})" - VisualDescription is always surfaced explicitly when present so the planner has something authoritative to stay consistent with.</summary>
    private static string FormatCharacterLine(StoryCharacter character)
    {
        var line = string.IsNullOrWhiteSpace(character.Description)
            ? character.Name
            : $"{character.Name}: {character.Description}";

        return string.IsNullOrWhiteSpace(character.VisualDescription)
            ? line
            : $"{line} (Appearance: {character.VisualDescription})";
    }

    /// <summary>Same format as <see cref="FormatCharacterLine"/>, for locations.</summary>
    private static string FormatLocationLine(StoryLocation location)
    {
        var line = string.IsNullOrWhiteSpace(location.Description)
            ? location.Name
            : $"{location.Name}: {location.Description}";

        return string.IsNullOrWhiteSpace(location.VisualDescription)
            ? line
            : $"{line} (Appearance: {location.VisualDescription})";
    }
}
