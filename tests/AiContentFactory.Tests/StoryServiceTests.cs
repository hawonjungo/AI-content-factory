using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryServiceTests
{
    private static (StoryService Service, FakeStoryRepository Repo) Build()
    {
        var repo = new FakeStoryRepository();
        var service = new StoryService(repo);
        return (service, repo);
    }

    [Fact]
    public async Task Creating_a_story_persists_it_and_returns_its_fields()
    {
        var (service, repo) = Build();

        var response = await service.CreateAsync(new CreateStoryRequest("My Series", "A premise", "drama", "en", "9:16"));

        Assert.Equal("My Series", response.Title);
        Assert.Equal("A premise", response.Premise);
        Assert.Equal("drama", response.Niche);
        Assert.True(repo.Stories.ContainsKey(response.Id));
    }

    // ---- Series style preset (StylePresetId) ----

    [Fact]
    public async Task Creating_a_story_without_a_style_leaves_StylePresetId_null()
    {
        var (service, repo) = Build();

        var response = await service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null));

        Assert.Null(response.StylePresetId);
        Assert.Null(repo.Stories[response.Id].StylePresetId);
    }

    [Fact]
    public async Task Creating_a_story_with_a_catalog_style_round_trips_it_through_create_and_get()
    {
        var (service, repo) = Build();

        var created = await service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null, "pixar-3d"));
        var fetched = await service.GetByIdAsync(created.Id);

        Assert.Equal("pixar-3d", created.StylePresetId);
        Assert.Equal("pixar-3d", repo.Stories[created.Id].StylePresetId);
        Assert.Equal("pixar-3d", fetched!.StylePresetId);
    }

    [Fact]
    public async Task Creating_a_story_stores_the_canonical_catalog_id_for_a_differently_cased_or_padded_id()
    {
        var (service, _) = Build();

        var created = await service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null, "  PIXAR-3D "));

        Assert.Equal("pixar-3d", created.StylePresetId);
    }

    [Fact]
    public async Task Creating_a_story_with_a_blank_style_means_no_style()
    {
        var (service, _) = Build();

        var created = await service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null, "   "));

        Assert.Null(created.StylePresetId);
    }

    [Fact]
    public async Task Creating_a_story_with_an_unknown_style_id_is_rejected_with_the_preset_validation_message_and_persists_nothing()
    {
        var (service, repo) = Build();

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null, "no-such-style")));

        Assert.Equal("Unknown style preset 'no-such-style'.", ex.Message);
        Assert.Empty(repo.Stories);
    }

    [Fact]
    public async Task Updating_a_story_can_set_and_change_its_style()
    {
        var (service, repo) = Build();
        var created = await service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null));

        var set = await service.UpdateAsync(created.Id, new UpdateStoryRequest("My Series", null, null, "anime"));
        Assert.Equal("anime", set!.StylePresetId);
        Assert.Equal("anime", repo.Stories[created.Id].StylePresetId);

        var changed = await service.UpdateAsync(created.Id, new UpdateStoryRequest("My Series", null, null, "pixar-3d"));
        Assert.Equal("pixar-3d", changed!.StylePresetId);
    }

    [Fact]
    public async Task Updating_a_story_with_a_null_style_leaves_the_existing_style_unchanged()
    {
        var (service, repo) = Build();
        var created = await service.CreateAsync(new CreateStoryRequest("My Series", "old premise", "drama", null, null, "pixar-3d"));

        // A client that predates the field (or simply omits it) re-saves the other details.
        var updated = await service.UpdateAsync(created.Id, new UpdateStoryRequest("Renamed", "new premise", null));

        Assert.Equal("pixar-3d", updated!.StylePresetId);
        Assert.Equal("pixar-3d", repo.Stories[created.Id].StylePresetId);
        Assert.Equal("Renamed", updated.Title);
        Assert.Equal("new premise", updated.Premise);
        Assert.Null(updated.Niche); // Premise/Niche keep their plain PUT semantics
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Updating_a_story_with_a_blank_style_clears_it(string blank)
    {
        var (service, repo) = Build();
        var created = await service.CreateAsync(new CreateStoryRequest("My Series", null, null, null, null, "pixar-3d"));

        var updated = await service.UpdateAsync(created.Id, new UpdateStoryRequest("My Series", null, null, blank));

        Assert.Null(updated!.StylePresetId);
        Assert.Null(repo.Stories[created.Id].StylePresetId);
    }

    [Fact]
    public async Task Updating_a_story_with_an_unknown_style_is_rejected_and_changes_nothing()
    {
        var (service, repo) = Build();
        var created = await service.CreateAsync(new CreateStoryRequest("My Series", "premise", null, null, null, "pixar-3d"));

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.UpdateAsync(created.Id, new UpdateStoryRequest("Renamed", "other", null, "no-such-style")));

        Assert.Equal("Unknown style preset 'no-such-style'.", ex.Message);
        var story = repo.Stories[created.Id];
        Assert.Equal("pixar-3d", story.StylePresetId);
        Assert.Equal("My Series", story.Title); // nothing else was applied either
        Assert.Equal("premise", story.Premise);
    }

    [Fact]
    public async Task Style_field_json_contract_is_stylePresetId_and_is_optional_on_requests()
    {
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var (service, _) = Build();

        // Requests from a client that does not know the field still bind (property omitted -> null).
        var legacyUpdate = System.Text.Json.JsonSerializer.Deserialize<UpdateStoryRequest>("""{"title":"T","premise":"P","niche":"N"}""", web);
        var legacyCreate = System.Text.Json.JsonSerializer.Deserialize<CreateStoryRequest>("""{"title":"T"}""", web);
        Assert.Null(legacyUpdate!.StylePresetId);
        Assert.Null(legacyCreate!.StylePresetId);

        var update = System.Text.Json.JsonSerializer.Deserialize<UpdateStoryRequest>("""{"title":"T","stylePresetId":"anime"}""", web);
        Assert.Equal("anime", update!.StylePresetId);

        var response = await service.CreateAsync(new CreateStoryRequest("T", null, null, null, null, "anime"));
        var json = System.Text.Json.JsonSerializer.Serialize(response, web);
        Assert.Contains("\"stylePresetId\":\"anime\"", json);
    }

    [Fact]
    public async Task Updating_an_unknown_story_returns_null_even_with_a_style()
    {
        var (service, _) = Build();

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateStoryRequest("x", null, null, "anime"));

        Assert.Null(result);
    }

    [Fact]
    public async Task Creating_an_episode_for_an_unknown_story_returns_null()
    {
        var (service, _) = Build();

        var result = await service.CreateEpisodeAsync(Guid.NewGuid(), new CreateStoryEpisodeRequest(1, "Ep 1", null));

        Assert.Null(result);
    }

    [Fact]
    public async Task Creating_an_episode_happy_path_starts_as_draft()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;

        var response = await service.CreateEpisodeAsync(story.Id, new CreateStoryEpisodeRequest(1, "Pilot", null));

        Assert.NotNull(response);
        Assert.Equal(story.Id, response!.StoryId);
        Assert.Equal(1, response.EpisodeNumber);
        Assert.Equal("Pilot", response.Title);
        Assert.Equal(nameof(StoryEpisodeStatus.Draft), response.Status);
        Assert.Null(response.Script);
        Assert.Null(response.PreviousEpisodeId);
        Assert.True(repo.Episodes.ContainsKey(response.Id));
    }

    [Fact]
    public async Task A_duplicate_episode_number_within_the_same_story_is_rejected()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        var existing = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        repo.Episodes[existing.Id] = existing;

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.CreateEpisodeAsync(story.Id, new CreateStoryEpisodeRequest(1, "Pilot Redux", null)));

        Assert.Contains("1", ex.Message);
        Assert.Single(repo.Episodes);
    }

    [Fact]
    public async Task An_unknown_PreviousEpisodeId_is_rejected()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;

        await Assert.ThrowsAsync<DomainException>(
            () => service.CreateEpisodeAsync(story.Id, new CreateStoryEpisodeRequest(2, "Ep 2", Guid.NewGuid())));

        Assert.Empty(repo.Episodes);
    }

    [Fact]
    public async Task A_PreviousEpisodeId_belonging_to_a_different_story_is_rejected()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        var otherStory = Story.Create("Other Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        repo.Stories[otherStory.Id] = otherStory;
        var episodeOfOtherStory = StoryEpisode.Create(otherStory.Id, 1, "Other Ep 1", null);
        repo.Episodes[episodeOfOtherStory.Id] = episodeOfOtherStory;

        await Assert.ThrowsAsync<DomainException>(
            () => service.CreateEpisodeAsync(story.Id, new CreateStoryEpisodeRequest(1, "Ep 1", episodeOfOtherStory.Id)));
    }

    [Fact]
    public async Task Setting_an_episode_script_manually_persists_it_and_advances_status_without_calling_AI()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        repo.Episodes[episode.Id] = episode;

        var response = await service.SetEpisodeScriptAsync(story.Id, episode.Id, new UpdateStoryEpisodeScriptRequest("A manually-edited script."));

        Assert.NotNull(response);
        Assert.Equal("A manually-edited script.", response!.Script);
        Assert.Equal(nameof(StoryEpisodeStatus.Scripted), response.Status);
        Assert.Equal("A manually-edited script.", episode.Script);
    }

    [Fact]
    public async Task Setting_an_episode_script_for_an_unknown_episode_returns_null()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;

        var response = await service.SetEpisodeScriptAsync(story.Id, Guid.NewGuid(), new UpdateStoryEpisodeScriptRequest("Some script."));

        Assert.Null(response);
    }

    [Fact]
    public async Task Setting_an_episode_script_for_an_episode_belonging_to_a_different_story_returns_null()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        var otherStory = Story.Create("Other Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        repo.Stories[otherStory.Id] = otherStory;
        var episodeOfOtherStory = StoryEpisode.Create(otherStory.Id, 1, "Other Ep 1", null);
        repo.Episodes[episodeOfOtherStory.Id] = episodeOfOtherStory;

        var response = await service.SetEpisodeScriptAsync(story.Id, episodeOfOtherStory.Id, new UpdateStoryEpisodeScriptRequest("Some script."));

        Assert.Null(response);
    }

    [Fact]
    public async Task Setting_a_blank_episode_script_is_rejected()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", null);
        repo.Episodes[episode.Id] = episode;

        await Assert.ThrowsAsync<DomainException>(
            () => service.SetEpisodeScriptAsync(story.Id, episode.Id, new UpdateStoryEpisodeScriptRequest("   ")));
    }

    [Fact]
    public async Task Updating_a_character_persists_the_new_fields_and_behavior_profile()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        var character = StoryCharacter.Create(story.Id, "Milo", "A curious cat", "orange tabby");
        repo.Characters[character.Id] = character;
        story.AttachCharacter(character);

        var response = await service.UpdateCharacterAsync(
            story.Id,
            character.Id,
            new UpdateStoryCharacterRequest("Milo", "A curious cat", "orange tabby", CharacterBehaviorProfile.AnthropomorphicCat));

        Assert.NotNull(response);
        Assert.Equal(nameof(CharacterBehaviorProfile.AnthropomorphicCat), response!.BehaviorProfile);
        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, character.BehaviorProfile);
    }

    [Fact]
    public async Task Updating_a_character_for_an_unknown_story_returns_null()
    {
        var (service, _) = Build();

        var response = await service.UpdateCharacterAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateStoryCharacterRequest("Milo", null, null, CharacterBehaviorProfile.AnthropomorphicCat));

        Assert.Null(response);
    }

    [Fact]
    public async Task Updating_an_unknown_character_returns_null()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;

        var response = await service.UpdateCharacterAsync(
            story.Id,
            Guid.NewGuid(),
            new UpdateStoryCharacterRequest("Milo", null, null, CharacterBehaviorProfile.AnthropomorphicCat));

        Assert.Null(response);
    }

    [Fact]
    public async Task Getting_state_for_an_unknown_story_returns_null()
    {
        var (service, _) = Build();

        var result = await service.GetStateAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task Getting_state_the_first_time_lazily_creates_and_persists_an_empty_state()
    {
        var (service, repo) = Build();
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;

        var response = await service.GetStateAsync(story.Id);

        Assert.NotNull(response);
        Assert.Equal(story.Id, response!.StoryId);
        Assert.Null(response.CurrentLocation);
        Assert.Empty(response.ImportantEvents);
        Assert.Single(repo.States);

        // Second call reuses the persisted row rather than creating another one.
        var second = await service.GetStateAsync(story.Id);
        Assert.Single(repo.States);
        Assert.Equal(response.StoryId, second!.StoryId);
    }
}
