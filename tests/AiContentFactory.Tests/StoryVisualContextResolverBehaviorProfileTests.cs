using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers the REAL <see cref="StoryVisualContextResolver.GetCharacterBehaviorProfilesAsync"/>
/// (not the test double) - the production reverse-lookup that turns a Story
/// "bible"'s <see cref="StoryCharacter.BehaviorProfile"/> values into the
/// name -&gt; profile dictionary consumed by <see cref="AiContentFactory.Application.Generation.ImagePromptComposer"/>
/// and <see cref="AiContentFactory.Application.Generation.VideoPromptBuilder"/>.
/// Same "null when not Story-linked" contract as its sibling
/// <see cref="IStoryVisualContextResolver.GetCastAndLocationVisualDescriptionsAsync"/>.
/// </summary>
public class StoryVisualContextResolverBehaviorProfileTests
{
    private static (StoryVisualContextResolver Resolver, FakeStoryRepository Repo) Build()
    {
        var repo = new FakeStoryRepository();
        return (new StoryVisualContextResolver(repo), repo);
    }

    [Fact]
    public async Task Non_story_linked_project_returns_null()
    {
        var (resolver, _) = Build();

        var result = await resolver.GetCharacterBehaviorProfilesAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task Story_linked_project_with_no_character_opted_in_returns_null()
    {
        var (resolver, repo) = Build();
        var projectId = Guid.NewGuid();
        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        var character = StoryCharacter.Create(story.Id, "Nova", "witty", "Orange fur"); // default None
        story.AttachCharacter(character);
        repo.Stories[story.Id] = story;
        repo.Characters[character.Id] = character;
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.LinkContentProject(projectId);
        repo.Episodes[episode.Id] = episode;

        var result = await resolver.GetCharacterBehaviorProfilesAsync(projectId);

        Assert.Null(result);
    }

    [Fact]
    public async Task Story_linked_project_with_one_opted_in_character_returns_only_that_character()
    {
        var (resolver, repo) = Build();
        var projectId = Guid.NewGuid();
        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        var milo = StoryCharacter.Create(story.Id, "Milo", "curious", "orange tabby", CharacterBehaviorProfile.AnthropomorphicCat);
        var mimi = StoryCharacter.Create(story.Id, "Mimi", "shy", "grey cat"); // default None - not opted in
        story.AttachCharacter(milo);
        story.AttachCharacter(mimi);
        repo.Stories[story.Id] = story;
        repo.Characters[milo.Id] = milo;
        repo.Characters[mimi.Id] = mimi;
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.LinkContentProject(projectId);
        repo.Episodes[episode.Id] = episode;

        var result = await resolver.GetCharacterBehaviorProfilesAsync(projectId);

        Assert.NotNull(result);
        var single = Assert.Single(result!);
        Assert.Equal("Milo", single.Key);
        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, single.Value);
        Assert.False(result!.ContainsKey("Mimi"));
    }

    [Fact]
    public async Task Lookup_is_case_insensitive()
    {
        var (resolver, repo) = Build();
        var projectId = Guid.NewGuid();
        var story = Story.Create("Nova's Cases", "A fox detective solves crimes.", "drama", "en", "9:16");
        var milo = StoryCharacter.Create(story.Id, "Milo", "curious", "orange tabby", CharacterBehaviorProfile.AnthropomorphicCat);
        story.AttachCharacter(milo);
        repo.Stories[story.Id] = story;
        repo.Characters[milo.Id] = milo;
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        episode.LinkContentProject(projectId);
        repo.Episodes[episode.Id] = episode;

        var result = await resolver.GetCharacterBehaviorProfilesAsync(projectId);

        Assert.NotNull(result);
        Assert.True(result!.TryGetValue("milo", out var profile));
        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, profile);
    }
}
