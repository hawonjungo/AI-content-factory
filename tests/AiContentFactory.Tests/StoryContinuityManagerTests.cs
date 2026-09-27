using AiContentFactory.Application.Stories;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryContinuityManagerTests
{
    private sealed class FakeStoryPlannerAgent : IStoryPlannerAgent
    {
        public StoryPlannerAgentOutput Output { get; set; } = new(
            "Premise", new() { "rule" }, "Characters", "Relationships", new() { "visual" }, "Tone", new() { "element" }, new() { "constraint" }, "Arc");

        /// <summary>Captures the input from the most recent call, so tests can pin exactly what data reached the agent.</summary>
        public StoryPlannerAgentInput? LastInput { get; private set; }

        public Task<StoryPlannerAgentOutput> GenerateAsync(StoryPlannerAgentInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(Output);
        }
    }

    private sealed class FakeEpisodePlannerAgent : IEpisodePlannerAgent
    {
        public EpisodePlannerAgentOutput Output { get; set; } = new(
            "Title", "Objective", "Setup", new() { "beat" }, "Conflict", "Escalation", "Resolution", "Cliffhanger", new() { "req" }, new() { "scene" });

        public Task<EpisodePlannerAgentOutput> GenerateAsync(EpisodePlannerAgentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Output);
    }

    private sealed class FakeStoryScriptWriterAgent : IStoryScriptWriterAgent
    {
        public StoryScriptWriterOutput Output { get; set; } = new(
            "Hook", "Introduction", "Body", "Escalation", "Payoff", "CallToAction", "Summary");

        public Task<StoryScriptWriterOutput> GenerateAsync(StoryScriptWriterAgentInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Output);
    }

    private sealed class FakeStoryContinuityAnalysisAgent : IStoryContinuityAnalysisAgent
    {
        public StoryContinuityAnalysisOutput Output { get; set; } = new(
            "New Location", "New Objective", "New CharacterStates",
            new() { "event" }, new() { "thread" }, new() { "conflict" }, new() { "fact" }, "Destination", "Notes");

        public int Calls { get; private set; }

        public Task<StoryContinuityAnalysisOutput> GenerateAsync(StoryContinuityAnalysisAgentInput input, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Output);
        }
    }

    private sealed class FakeContinuityValidatorAgent : IContinuityValidatorAgent
    {
        public ContinuityValidationOutput Output { get; set; } = new(new(), new(), 1.0);

        /// <summary>Captures the input from the most recent call, so tests can assert on the baseline state actually sent to the agent.</summary>
        public ContinuityValidatorAgentInput? LastInput { get; private set; }

        public Task<ContinuityValidationOutput> GenerateAsync(ContinuityValidatorAgentInput input, CancellationToken cancellationToken = default)
        {
            LastInput = input;
            return Task.FromResult(Output);
        }
    }

    private sealed class Harness
    {
        public FakeStoryRepository Repo { get; } = new();
        public FakeStoryPlannerAgent Planner { get; } = new();
        public FakeEpisodePlannerAgent EpisodePlanner { get; } = new();
        public FakeStoryScriptWriterAgent ScriptWriter { get; } = new();
        public FakeStoryContinuityAnalysisAgent Analysis { get; } = new();
        public FakeContinuityValidatorAgent Validator { get; } = new();
        public StoryContinuityManager Manager { get; }

        public Harness()
        {
            Manager = new StoryContinuityManager(Repo, Planner, EpisodePlanner, ScriptWriter, Analysis, Validator);
        }
    }

    [Fact]
    public async Task PlanStoryAsync_persists_the_generated_bible()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;

        var response = await h.Manager.PlanStoryAsync(story.Id, new PlanStoryBibleRequest(null, null, null, null, null, null, null, null));

        Assert.NotNull(response);
        Assert.Equal("Premise", response!.Premise);
        Assert.NotNull(story.Bible);
    }

    [Fact]
    public async Task PlanStoryAsync_returns_null_for_an_unknown_story()
    {
        var h = new Harness();

        var response = await h.Manager.PlanStoryAsync(Guid.NewGuid(), new PlanStoryBibleRequest(null, null, null, null, null, null, null, null));

        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateBibleAsync_replaces_the_bible_wholesale_without_calling_the_planner_agent()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        story.SetBible(StoryBible.Create(
            "Old premise", new[] { "old rule" }, "Old chars", "Old relationships",
            new[] { "Mimi must always be drawn exactly as: grey short-haired cat" },
            "Old tone", new[] { "old element" }, new[] { "old constraint" }, "Old arc"));
        h.Repo.Stories[story.Id] = story;

        var request = new UpdateStoryBibleRequest(
            Premise: "Old premise",
            WorldRules: new[] { "old rule" },
            CharacterDefinitions: "Old chars",
            CharacterRelationships: "Old relationships",
            VisualConsistencyRules: new[] { "Mimi must always be drawn exactly as: white long-haired Persian cat, blue eyes" },
            Tone: "Old tone",
            RecurringElements: new[] { "old element" },
            StoryConstraints: new[] { "old constraint" },
            StoryArc: "Old arc");

        var response = await h.Manager.UpdateBibleAsync(story.Id, request);

        Assert.NotNull(response);
        Assert.Contains("white long-haired Persian cat", response!.VisualConsistencyRules.Single());
        Assert.Contains("white long-haired Persian cat", story.Bible!.VisualConsistencyRules.Single());
        Assert.Null(h.Planner.LastInput); // no LLM call made for a plain edit
    }

    [Fact]
    public async Task UpdateBibleAsync_returns_null_for_an_unknown_story()
    {
        var h = new Harness();

        var response = await h.Manager.UpdateBibleAsync(
            Guid.NewGuid(),
            new UpdateStoryBibleRequest(null, null, null, null, null, null, null, null, null));

        Assert.Null(response);
    }

    /// <summary>
    /// Regression test for the Bible-generation data-flow bug: a persisted
    /// StoryCharacter's VisualDescription must reach the planner agent's
    /// input, not be silently dropped in favor of the free-text request
    /// body. Pins the actual data sent, not just that the call succeeds.
    /// </summary>
    [Fact]
    public async Task PlanStoryAsync_sends_the_persisted_characters_visual_description_to_the_planner_agent()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        var character = StoryCharacter.Create(story.Id, "Milo", "A curious housecat.", "grey short-haired cat, amber eyes");
        story.AttachCharacter(character);
        h.Repo.Stories[story.Id] = story;
        h.Repo.Characters[character.Id] = character;

        var request = new PlanStoryBibleRequest(null, null, null, null, new[] { "Milo" }, null, null, null);
        await h.Manager.PlanStoryAsync(story.Id, request);

        Assert.NotNull(h.Planner.LastInput);
        Assert.NotNull(h.Planner.LastInput!.MainCharacters);
        Assert.Contains(h.Planner.LastInput!.MainCharacters!, line =>
            line.Contains("Milo") && line.Contains("grey short-haired cat, amber eyes"));
    }

    /// <summary>
    /// Same fix, for locations: a persisted StoryLocation's VisualDescription
    /// must reach the planner agent's input.
    /// </summary>
    [Fact]
    public async Task PlanStoryAsync_sends_the_persisted_locations_visual_description_to_the_planner_agent()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        var location = StoryLocation.Create(story.Id, "The Attic", "A cluttered storage space.", "dusty wooden beams, single round window");
        story.AttachLocation(location);
        h.Repo.Stories[story.Id] = story;
        h.Repo.Locations[location.Id] = location;

        var request = new PlanStoryBibleRequest(null, null, null, null, null, new[] { "The Attic" }, null, null);
        await h.Manager.PlanStoryAsync(story.Id, request);

        Assert.NotNull(h.Planner.LastInput);
        Assert.NotNull(h.Planner.LastInput!.Locations);
        Assert.Contains(h.Planner.LastInput!.Locations!, line =>
            line.Contains("The Attic") && line.Contains("dusty wooden beams, single round window"));
    }

    /// <summary>
    /// Precedence: when persisted characters exist, the free-text
    /// request.MainCharacters lines are appended as additional context
    /// rather than silently dropped - both sources reach the agent.
    /// </summary>
    [Fact]
    public async Task PlanStoryAsync_appends_request_body_characters_alongside_persisted_characters_when_both_are_supplied()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        var character = StoryCharacter.Create(story.Id, "Milo", null, "grey short-haired cat, amber eyes");
        story.AttachCharacter(character);
        h.Repo.Stories[story.Id] = story;
        h.Repo.Characters[character.Id] = character;

        var request = new PlanStoryBibleRequest(null, null, null, null, new[] { "Extra note: Milo loves tuna." }, null, null, null);
        await h.Manager.PlanStoryAsync(story.Id, request);

        Assert.NotNull(h.Planner.LastInput);
        var lines = h.Planner.LastInput!.MainCharacters!;
        Assert.Contains(lines, line => line.Contains("grey short-haired cat, amber eyes"));
        Assert.Contains(lines, line => line.Contains("Extra note: Milo loves tuna."));
    }

    /// <summary>
    /// Fallback path: when no persisted characters exist yet, the free-text
    /// request.MainCharacters remains the sole source - generating a Bible
    /// before creating any character records must keep working.
    /// </summary>
    [Fact]
    public async Task PlanStoryAsync_falls_back_to_request_body_characters_when_no_persisted_characters_exist()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;

        var request = new PlanStoryBibleRequest(null, null, null, null, new[] { "Milo: a cat" }, null, null, null);
        await h.Manager.PlanStoryAsync(story.Id, request);

        Assert.NotNull(h.Planner.LastInput);
        Assert.Equal(new[] { "Milo: a cat" }, h.Planner.LastInput!.MainCharacters);
    }

    [Fact]
    public async Task PlanEpisodeAsync_throws_when_the_story_has_no_bible_yet()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        h.Repo.Episodes[episode.Id] = episode;

        await Assert.ThrowsAsync<DomainException>(() => h.Manager.PlanEpisodeAsync(story.Id, episode.Id));
    }

    [Fact]
    public async Task PlanEpisodeAsync_persists_the_generated_outline_once_a_bible_exists()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        story.SetBible(StoryBible.Create("p", null, "c", "r", null, "t", null, null, null));
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        h.Repo.Episodes[episode.Id] = episode;

        var response = await h.Manager.PlanEpisodeAsync(story.Id, episode.Id);

        Assert.NotNull(response);
        Assert.Equal("Title", response!.Title);
        Assert.NotNull(episode.Outline);
    }

    [Fact]
    public async Task WriteScriptAsync_throws_when_the_episode_has_no_outline_yet()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        h.Repo.Episodes[episode.Id] = episode;

        await Assert.ThrowsAsync<DomainException>(() => h.Manager.WriteScriptAsync(story.Id, episode.Id));
    }

    [Fact]
    public async Task WriteScriptAsync_composes_and_persists_the_joined_script_and_summary()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode.SetOutline(StoryEpisodeOutline.Create("t", "o", "s", new[] { "beat" }, "c", "e", "r", "cl", null, null));
        h.Repo.Episodes[episode.Id] = episode;

        var response = await h.Manager.WriteScriptAsync(story.Id, episode.Id);

        Assert.NotNull(response);
        Assert.Contains("HOOK:", response!.Script);
        Assert.Contains("CALL TO ACTION:", response.Script);
        Assert.Equal("Summary", response.Summary);
    }

    [Fact]
    public async Task FinalizeEpisodeAsync_when_invalid_does_not_mutate_state_or_complete_the_episode()
    {
        var h = new Harness();
        h.Validator.Output = new ContinuityValidationOutput(
            Warnings: new(),
            CriticalIssues: new List<ContinuityIssue> { new(ContinuityCategories.Location, ContinuityIssueTypes.ImpossibleLocationTransition, "Bad teleport.") },
            Score: 0.3);

        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode.Id] = episode;

        var response = await h.Manager.FinalizeEpisodeAsync(story.Id, episode.Id);

        Assert.NotNull(response);
        Assert.False(response!.Valid);
        Assert.Empty(response.Warnings);
        Assert.Single(response.CriticalIssues);
        Assert.Null(response.Episode);
        Assert.Equal(StoryEpisodeStatus.Scripted, episode.Status); // must NOT reach Completed
        Assert.Empty(h.Repo.States); // StoryState must not have been created/touched
        Assert.Equal(0, h.Analysis.Calls); // the analysis agent must never run when validation fails
    }

    /// <summary>
    /// Pins the crux "ignore warning" UX behavior: Valid is computed from
    /// CriticalIssues.Count == 0, NOT from whether Warnings is empty. A
    /// result with warnings but zero critical issues must be treated as
    /// valid and must proceed to mutate StoryState/complete the episode.
    /// </summary>
    [Fact]
    public async Task FinalizeEpisodeAsync_with_only_warnings_and_no_critical_issues_is_valid_and_proceeds()
    {
        var h = new Harness();
        h.Validator.Output = new ContinuityValidationOutput(
            Warnings: new List<ContinuityIssue> { new(ContinuityCategories.Plot, ContinuityIssueTypes.UnresolvedThreadForgotten, "Thread drifting but not abandoned.") },
            CriticalIssues: new(),
            Score: 0.8);

        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode.Id] = episode;

        var response = await h.Manager.FinalizeEpisodeAsync(story.Id, episode.Id);

        Assert.NotNull(response);
        Assert.True(response!.Valid); // valid despite a non-empty Warnings list
        Assert.Single(response.Warnings);
        Assert.Empty(response.CriticalIssues);
        Assert.NotNull(response.Episode);
        Assert.Equal(StoryEpisodeStatus.Completed, episode.Status);
        Assert.Equal(1, h.Analysis.Calls); // must still run - warnings alone never block finalize
    }

    [Fact]
    public async Task FinalizeEpisodeAsync_when_valid_updates_state_completes_the_episode_and_freezes_a_snapshot()
    {
        var h = new Harness();
        h.Validator.Output = new ContinuityValidationOutput(new(), new(), 1.0);

        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode.Id] = episode;

        var response = await h.Manager.FinalizeEpisodeAsync(story.Id, episode.Id);

        Assert.NotNull(response);
        Assert.True(response!.Valid);
        Assert.Empty(response.Warnings);
        Assert.Empty(response.CriticalIssues);
        Assert.NotNull(response.Episode);
        Assert.Equal(StoryEpisodeStatus.Completed, episode.Status);
        Assert.NotNull(episode.StoryStateSnapshot);
        Assert.Equal("New Location", episode.StoryStateSnapshot!.CurrentLocation);
        Assert.Single(h.Repo.States);
        Assert.Equal("New Location", h.Repo.States.Values.Single().CurrentLocation);
        Assert.Equal(1, h.Analysis.Calls);
    }

    [Fact]
    public async Task FinalizeEpisodeAsync_throws_when_the_episode_has_no_script_yet()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        h.Repo.Episodes[episode.Id] = episode;

        await Assert.ThrowsAsync<DomainException>(() => h.Manager.FinalizeEpisodeAsync(story.Id, episode.Id));
    }

    [Fact]
    public async Task FinalizeEpisodeAsync_throws_when_the_previous_episode_has_not_been_completed_yet()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;
        var episode1 = StoryEpisode.Create(story.Id, 1, "Ep 1", null); // never finalized
        episode1.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode1.Id] = episode1;
        var episode2 = StoryEpisode.Create(story.Id, 2, "Ep 2", episode1.Id);
        episode2.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode2.Id] = episode2;

        await Assert.ThrowsAsync<DomainException>(() => h.Manager.FinalizeEpisodeAsync(story.Id, episode2.Id));
        Assert.Equal(0, h.Analysis.Calls); // must never reach the state-advancing analysis step
    }

    /// <summary>
    /// Regression test for the stale-global-StoryState bug: reproduces QA's
    /// scenario where re-validating an earlier, already-completed episode
    /// after later episodes have advanced the story must NOT see the later
    /// episodes' state. A 3-episode chain (not just 2) is required to prove
    /// the mechanism unambiguously - Episode 1 has no PreviousEpisodeId, so
    /// by design it always falls back to the live StoryState row regardless
    /// of this fix (there is no frozen predecessor snapshot to prefer). Using
    /// Episode 2 (whose true predecessor is Episode 1) as the episode being
    /// re-validated, with Episode 3 finalized afterwards to advance the live
    /// state further, isolates exactly what changed: before the fix,
    /// ValidateEpisodeAsync always read the live row (which would incorrectly
    /// reflect Episode 3's state); after the fix, it reads Episode 1's frozen
    /// StoryStateSnapshot instead, which is what Episode 2 should actually be
    /// validated against.
    /// </summary>
    [Fact]
    public async Task ValidateEpisodeAsync_on_an_earlier_episode_uses_its_own_predecessor_snapshot_not_the_live_state_advanced_by_later_episodes()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;

        // Episode 1 -> finalize with distinct state A.
        var episode1 = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode1.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode1.Id] = episode1;
        h.Analysis.Output = new StoryContinuityAnalysisOutput(
            "Da Nang", "ObjectiveA", "CharA", new() { "eventA" }, new() { "threadA" }, new() { "conflictA" }, new() { "factA" }, "DestA", "NotesA");
        var finalize1 = await h.Manager.FinalizeEpisodeAsync(story.Id, episode1.Id);
        Assert.True(finalize1!.Valid);

        // Episode 2 -> finalize with distinct state B. Its predecessor is Episode 1.
        var episode2 = StoryEpisode.Create(story.Id, 2, "Ep 2", episode1.Id);
        episode2.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode2.Id] = episode2;
        h.Analysis.Output = new StoryContinuityAnalysisOutput(
            "Hoi An", "ObjectiveB", "CharB", new() { "eventB" }, new() { "threadB" }, new() { "conflictB" }, new() { "factB" }, "DestB", "NotesB");
        var finalize2 = await h.Manager.FinalizeEpisodeAsync(story.Id, episode2.Id);
        Assert.True(finalize2!.Valid);

        // Episode 3 -> finalize with distinct state C. This advances the live
        // StoryState row past Episode 2's own baseline.
        var episode3 = StoryEpisode.Create(story.Id, 3, "Ep 3", episode2.Id);
        episode3.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode3.Id] = episode3;
        h.Analysis.Output = new StoryContinuityAnalysisOutput(
            "Hue", "ObjectiveC", "CharC", new() { "eventC" }, new() { "threadC" }, new() { "conflictC" }, new() { "factC" }, "DestC", "NotesC");
        var finalize3 = await h.Manager.FinalizeEpisodeAsync(story.Id, episode3.Id);
        Assert.True(finalize3!.Valid);

        // Sanity check: the live StoryState row has indeed moved on to Episode 3's state.
        Assert.Equal("Hue", h.Repo.States.Values.Single().CurrentLocation);

        // Re-validate Episode 2 (simulating a regenerate-then-revalidate of an
        // earlier episode). Its baseline must be Episode 1's frozen snapshot
        // ("Da Nang"), never the live state that Episode 3 advanced to ("Hue").
        var revalidation = await h.Manager.ValidateEpisodeAsync(story.Id, episode2.Id);

        Assert.NotNull(revalidation);
        Assert.NotNull(h.Validator.LastInput);
        Assert.Equal("Da Nang", h.Validator.LastInput!.CurrentLocation);
        Assert.NotEqual("Hue", h.Validator.LastInput!.CurrentLocation);
        Assert.Equal("ObjectiveA", h.Validator.LastInput!.CurrentObjective);
        Assert.Contains("threadA", h.Validator.LastInput!.OpenStoryThreads!);
    }

    /// <summary>
    /// Confirms the normal forward-generation flow (validating the very next
    /// new episode, i.e. the "frontier") is unaffected by the fix: at that
    /// moment the previous episode's frozen snapshot and the live StoryState
    /// row are identical (both were derived from the same finalize call), so
    /// the resolved baseline is the same either way.
    /// </summary>
    [Fact]
    public async Task ValidateEpisodeAsync_on_the_next_new_episode_uses_the_previous_episodes_state_same_as_before_the_fix()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;

        var episode1 = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode1.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode1.Id] = episode1;
        h.Analysis.Output = new StoryContinuityAnalysisOutput(
            "Da Nang", "ObjectiveA", "CharA", new() { "eventA" }, new() { "threadA" }, new() { "conflictA" }, new() { "factA" }, "DestA", "NotesA");
        var finalize1 = await h.Manager.FinalizeEpisodeAsync(story.Id, episode1.Id);
        Assert.True(finalize1!.Valid);

        // Episode 2 is the frontier: the very next new episode, not yet finalized.
        var episode2 = StoryEpisode.Create(story.Id, 2, "Ep 2", episode1.Id);
        episode2.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode2.Id] = episode2;

        var validation = await h.Manager.ValidateEpisodeAsync(story.Id, episode2.Id);

        Assert.NotNull(validation);
        Assert.NotNull(h.Validator.LastInput);
        Assert.Equal("Da Nang", h.Validator.LastInput!.CurrentLocation); // same value the live StoryState row also holds at this point
        Assert.Equal(h.Repo.States.Values.Single().CurrentLocation, h.Validator.LastInput!.CurrentLocation);
    }

    /// <summary>
    /// Regression test for the genesis-episode gap: Episode 1 has no
    /// PreviousEpisodeId, so by definition nothing precedes it - its baseline
    /// must always be empty, never the live StoryState row. Reproduces
    /// regenerating/revalidating Episode 1 AFTER Episode 2 has already
    /// finalized and advanced the live state: before this fix,
    /// ResolveBaselineStateAsync fell back to the live StoryState row
    /// whenever there was no completed predecessor, which for Episode 1
    /// specifically would incorrectly surface Episode 2's advanced state (or
    /// even Episode 1's own prior content on a second revalidation) instead
    /// of "nothing precedes this episode".
    /// </summary>
    [Fact]
    public async Task ValidateEpisodeAsync_on_episode_one_always_uses_empty_baseline_even_after_a_later_episode_has_advanced_the_live_state()
    {
        var h = new Harness();
        var story = Story.Create("My Series", null, null, null, null);
        h.Repo.Stories[story.Id] = story;

        // Episode 1 -> finalize with distinct state A.
        var episode1 = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode1.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode1.Id] = episode1;
        h.Analysis.Output = new StoryContinuityAnalysisOutput(
            "Da Nang", "ObjectiveA", "CharA", new() { "eventA" }, new() { "threadA" }, new() { "conflictA" }, new() { "factA" }, "DestA", "NotesA");
        var finalize1 = await h.Manager.FinalizeEpisodeAsync(story.Id, episode1.Id);
        Assert.True(finalize1!.Valid);

        // Episode 2 -> finalize with distinct state B. This advances the live
        // StoryState row past Episode 1's own (nonexistent) baseline.
        var episode2 = StoryEpisode.Create(story.Id, 2, "Ep 2", episode1.Id);
        episode2.SetScript("HOOK:\n...");
        h.Repo.Episodes[episode2.Id] = episode2;
        h.Analysis.Output = new StoryContinuityAnalysisOutput(
            "Hoi An", "ObjectiveB", "CharB", new() { "eventB" }, new() { "threadB" }, new() { "conflictB" }, new() { "factB" }, "DestB", "NotesB");
        var finalize2 = await h.Manager.FinalizeEpisodeAsync(story.Id, episode2.Id);
        Assert.True(finalize2!.Valid);

        // Sanity check: the live StoryState row has indeed moved on to Episode 2's state.
        Assert.Equal("Hoi An", h.Repo.States.Values.Single().CurrentLocation);

        // Re-validate Episode 1 (simulating a regenerate-then-revalidate of the
        // genesis episode). Its baseline must be empty - not Episode 2's
        // advanced state, and not even Episode 1's own prior content, since
        // nothing precedes it.
        var revalidation = await h.Manager.ValidateEpisodeAsync(story.Id, episode1.Id);

        Assert.NotNull(revalidation);
        Assert.NotNull(h.Validator.LastInput);
        Assert.Null(h.Validator.LastInput!.CurrentLocation);
        Assert.NotEqual("Hoi An", h.Validator.LastInput!.CurrentLocation);
        Assert.Null(h.Validator.LastInput!.CurrentObjective);
        Assert.Empty(h.Validator.LastInput!.OpenStoryThreads!);
        Assert.Empty(h.Validator.LastInput!.UnresolvedConflicts!);
        Assert.Empty(h.Validator.LastInput!.KnownFacts!);
    }
}
