using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers the Part 3 gap: PromptAgentInput.StoryVisualContext must stay null
/// for every normal (non-Story) ContentProject - zero behaviour change - and
/// must be populated with the Story's Bible/cast visual context when the
/// ContentProject is linked to a Story episode.
/// </summary>
public class StoryboardServiceStoryContextTests
{
    private static (StoryboardService Service, FakeStoryboardRepository StoryboardRepo, FakePromptAgent PromptAgent, FakeStoryRepository StoryRepo)
        Build(ContentProject project)
    {
        var storyboardRepo = new FakeStoryboardRepository(Storyboard.Create(project.Id));
        var promptAgent = new FakePromptAgent();
        var projectRepo = new FakeContentProjectRepository(project);
        var storyRepo = new FakeStoryRepository();
        var resolver = new StoryVisualContextResolver(storyRepo);
        var service = new StoryboardService(storyboardRepo, promptAgent, projectRepo, resolver, NullLogger<StoryboardService>.Instance);
        return (service, storyboardRepo, promptAgent, storyRepo);
    }

    [Fact]
    public async Task Non_story_linked_project_leaves_StoryVisualContext_null()
    {
        var project = ContentProject.Create("Office Kitten", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, _) = Build(project);
        var scene = storyboardRepo.Current!.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        await service.SuggestScenePromptAsync(project.Id, scene.Id);

        Assert.NotNull(promptAgent.LastInput);
        Assert.Null(promptAgent.LastInput!.StoryVisualContext);
    }

    [Fact]
    public async Task Story_linked_project_populates_StoryVisualContext_from_the_bible_and_cast()
    {
        var project = ContentProject.Create("Nova Ep 1", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, storyRepo) = Build(project);
        var scene = storyboardRepo.Current!.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: new[] { "The city always looks rain-slicked at night" },
            tone: "noir, moody lighting", recurringElements: null, storyConstraints: null, storyArc: null);
        story.SetBible(bible);
        var character = StoryCharacter.Create(story.Id, "Nova", "witty", "Orange fur, blue trench coat.");
        story.AttachCharacter(character);
        storyRepo.Stories[story.Id] = story;
        storyRepo.Characters[character.Id] = character;

        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.SetScript("some script");
        episode.LinkContentProject(project.Id);
        storyRepo.Episodes[episode.Id] = episode;

        await service.SuggestScenePromptAsync(project.Id, scene.Id);

        Assert.NotNull(promptAgent.LastInput);
        var context = promptAgent.LastInput!.StoryVisualContext;
        Assert.False(string.IsNullOrWhiteSpace(context));
        Assert.Contains("The city always looks rain-slicked at night", context);
        Assert.Contains("Orange fur, blue trench coat.", context);
    }

    /// <summary>
    /// Regression coverage for a real incident (see
    /// <see cref="AiContentFactory.Application.Stories.StoryAssetReferenceService.AppendBibleGuidance"/>'s
    /// own remarks): a stale/self-referential Bible rule about a character
    /// must never reach the same prompt as that character's own, correct
    /// <see cref="StoryCharacter.VisualDescription"/> - the model has no way
    /// to know which one is authoritative and may pick the wrong one.
    /// </summary>
    [Fact]
    public async Task A_bible_rule_naming_a_character_with_its_own_VisualDescription_is_excluded_from_StoryVisualContext()
    {
        var project = ContentProject.Create("Nova Ep 1", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, storyRepo) = Build(project);
        var scene = storyboardRepo.Current!.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: new[] { "Nova always wears the blue coat" },
            tone: "noir, moody lighting", recurringElements: null, storyConstraints: null, storyArc: null);
        story.SetBible(bible);
        var character = StoryCharacter.Create(story.Id, "Nova", "witty", "Orange fur, blue trench coat.");
        story.AttachCharacter(character);
        storyRepo.Stories[story.Id] = story;
        storyRepo.Characters[character.Id] = character;

        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.SetScript("some script");
        episode.LinkContentProject(project.Id);
        storyRepo.Episodes[episode.Id] = episode;

        await service.SuggestScenePromptAsync(project.Id, scene.Id);

        var context = promptAgent.LastInput!.StoryVisualContext;
        Assert.False(string.IsNullOrWhiteSpace(context));
        Assert.DoesNotContain("Nova always wears the blue coat", context);
        Assert.Contains("Orange fur, blue trench coat.", context);
    }

    [Fact]
    public async Task Story_linked_project_whose_episode_has_a_StoryStateSnapshot_includes_location_objective_and_events()
    {
        var project = ContentProject.Create("Nova Ep 2", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, storyRepo) = Build(project);
        var scene = storyboardRepo.Current!.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        storyRepo.Stories[story.Id] = story;

        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.SetScript("some script");
        episode.LinkContentProject(project.Id);
        var snapshot = StoryStateSnapshot.Create(
            currentLocation: "Ha Long Bay",
            currentObjective: "Find the missing pearl",
            characterStates: null,
            importantEvents: new[] { "Nova arrived by boat", "Nova met a mysterious fisherman", "Nova found a clue" },
            openStoryThreads: null,
            unresolvedConflicts: null,
            knownFacts: null,
            nextPlannedDestination: null,
            notes: null);
        episode.Complete(snapshot);
        storyRepo.Episodes[episode.Id] = episode;

        await service.SuggestScenePromptAsync(project.Id, scene.Id);

        Assert.NotNull(promptAgent.LastInput);
        var context = promptAgent.LastInput!.StoryVisualContext;
        Assert.False(string.IsNullOrWhiteSpace(context));
        Assert.Contains("Ha Long Bay", context);
        Assert.Contains("Find the missing pearl", context);
        Assert.Contains("Nova met a mysterious fisherman", context);
        Assert.Contains("Nova found a clue", context);
    }

    [Fact]
    public async Task Story_linked_project_whose_episode_has_no_snapshot_yet_produces_the_same_context_as_before()
    {
        var project = ContentProject.Create("Nova Ep 1", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, storyRepo) = Build(project);
        var scene = storyboardRepo.Current!.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: new[] { "Nova always wears the blue coat" },
            tone: "noir, moody lighting", recurringElements: null, storyConstraints: null, storyArc: null);
        story.SetBible(bible);
        storyRepo.Stories[story.Id] = story;

        // Not yet Completed, so no StoryStateSnapshot exists.
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.SetScript("some script");
        episode.LinkContentProject(project.Id);
        storyRepo.Episodes[episode.Id] = episode;

        await service.SuggestScenePromptAsync(project.Id, scene.Id);

        Assert.NotNull(promptAgent.LastInput);
        var context = promptAgent.LastInput!.StoryVisualContext;
        Assert.False(string.IsNullOrWhiteSpace(context));
        Assert.Contains("Nova always wears the blue coat", context);
        Assert.DoesNotContain("Current scene setting", context);
        Assert.DoesNotContain("Current objective", context);
        Assert.DoesNotContain("Recent events", context);
    }
}
