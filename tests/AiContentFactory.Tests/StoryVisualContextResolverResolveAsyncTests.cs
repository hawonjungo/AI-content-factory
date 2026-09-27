using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="StoryVisualContextResolver.ResolveAsync"/>'s filtering
/// of the Story Bible's free-form <see cref="StoryBible.VisualConsistencyRules"/>
/// against each character/location's own canonical <see cref="StoryCharacter.VisualDescription"/>.
/// A stale or LLM-drifted Bible rule that names a character who already has
/// their own VisualDescription must never reach <c>PromptAgent</c> alongside
/// it - see <see cref="StoryAssetReferenceService.AppendBibleGuidance"/>'s
/// own remarks for the real incident (a Bible rule saying "Mimi must always
/// be a white cat" overriding her correct grey VisualDescription) this same
/// fix already guards against for the reference-image prompt.
/// </summary>
public class StoryVisualContextResolverResolveAsyncTests
{
    private static (StoryVisualContextResolver Resolver, FakeStoryRepository Repo) Build()
    {
        var repo = new FakeStoryRepository();
        return (new StoryVisualContextResolver(repo), repo);
    }

    private static (Story Story, Guid ProjectId) SeedStoryWithEpisode(FakeStoryRepository repo, Story story)
    {
        var projectId = Guid.NewGuid();
        repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.LinkContentProject(projectId);
        repo.Episodes[episode.Id] = episode;
        return (story, projectId);
    }

    [Fact]
    public async Task A_bible_rule_naming_a_character_with_its_own_VisualDescription_is_excluded()
    {
        var (resolver, repo) = Build();
        var story = Story.Create("Two Cats Traveling Around Vietnam", null, null, null, null);
        var mimi = StoryCharacter.Create(story.Id, "Mimi", "shy", "a sleek grey short-haired cat with amber eyes");
        story.AttachCharacter(mimi);
        story.SetBible(StoryBible.Create(
            premise: "Two cats travel Vietnam.",
            worldRules: null,
            characterDefinitions: null,
            characterRelationships: null,
            visualConsistencyRules: new[] { "Mimi must always be rendered as a majestic long-haired white Persian cat with blue eyes." },
            tone: null,
            recurringElements: null,
            storyConstraints: null,
            storyArc: null));
        var (_, projectId) = SeedStoryWithEpisode(repo, story);

        var context = await resolver.ResolveAsync(projectId);

        Assert.NotNull(context);
        Assert.DoesNotContain("white Persian", context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grey short-haired", context, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_bible_rule_naming_a_different_character_is_kept()
    {
        var (resolver, repo) = Build();
        var story = Story.Create("Two Cats Traveling Around Vietnam", null, null, null, null);
        var mimi = StoryCharacter.Create(story.Id, "Mimi", "shy", "grey short-haired cat");
        story.AttachCharacter(mimi);
        story.SetBible(StoryBible.Create(
            premise: "Two cats travel Vietnam.",
            worldRules: null,
            characterDefinitions: null,
            characterRelationships: null,
            visualConsistencyRules: new[] { "Milo always wears a tiny red backpack." },
            tone: null,
            recurringElements: null,
            storyConstraints: null,
            storyArc: null));
        var (_, projectId) = SeedStoryWithEpisode(repo, story);

        var context = await resolver.ResolveAsync(projectId);

        Assert.NotNull(context);
        Assert.Contains("Milo always wears a tiny red backpack", context);
    }

    [Fact]
    public async Task When_every_rule_is_excluded_the_visual_consistency_rules_section_is_omitted()
    {
        var (resolver, repo) = Build();
        var story = Story.Create("Two Cats Traveling Around Vietnam", null, null, null, null);
        var mimi = StoryCharacter.Create(story.Id, "Mimi", "shy", "grey short-haired cat");
        story.AttachCharacter(mimi);
        story.SetBible(StoryBible.Create(
            premise: "Two cats travel Vietnam.",
            worldRules: null,
            characterDefinitions: null,
            characterRelationships: null,
            visualConsistencyRules: new[] { "Mimi must always be a white Persian cat." },
            tone: null,
            recurringElements: null,
            storyConstraints: null,
            storyArc: null));
        var (_, projectId) = SeedStoryWithEpisode(repo, story);

        var context = await resolver.ResolveAsync(projectId);

        Assert.NotNull(context);
        Assert.DoesNotContain("Visual consistency rules", context);
    }
}
