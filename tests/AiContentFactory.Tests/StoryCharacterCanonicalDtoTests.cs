using System.Text.Json;
using System.Text.Json.Serialization;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The canonical character profile through the service/DTO layer: create sets
/// the fields, update treats null/omitted as "unchanged" and a blank string as
/// "clear", and the response exposes the reference-workflow state the UI needs.
/// </summary>
public class StoryCharacterCanonicalDtoTests
{
    // Mirrors Program.cs: ASP.NET web defaults + the string enum converter.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static (StoryService Service, FakeStoryRepository Repo, Story Story) Build()
    {
        var repo = new FakeStoryRepository();
        var service = new StoryService(repo);
        var story = Story.Create("My Series", null, null, null, null);
        repo.Stories[story.Id] = story;
        return (service, repo, story);
    }

    private static StoryCharacter Attach(FakeStoryRepository repo, Story story, StoryCharacter character)
    {
        repo.Characters[character.Id] = character;
        story.AttachCharacter(character);
        return character;
    }

    // ---- Create ----

    [Fact]
    public async Task Create_sets_the_canonical_fields_and_returns_them()
    {
        var (service, repo, story) = Build();

        var response = await service.CreateCharacterAsync(story.Id, new CreateStoryCharacterRequest(
            "Zephyr", "Curious", "Slate-blue fur", CharacterBehaviorProfile.None,
            CharacterKind.Animal, "  tabby cat ", " a red scarf ", "notched ear"));

        Assert.NotNull(response);
        Assert.Equal("Animal", response!.Kind);
        Assert.Equal("tabby cat", response.Species);
        Assert.Equal("a red scarf", response.ClothingAndAccessories);
        Assert.Equal("notched ear", response.DistinctiveFeatures);
        var stored = repo.Characters[response.Id];
        Assert.Equal(CharacterKind.Animal, stored.Kind);
        Assert.Equal("tabby cat", stored.Species);
    }

    [Fact]
    public async Task Create_without_the_new_fields_keeps_the_defaults()
    {
        var (service, _, story) = Build();

        var response = await service.CreateCharacterAsync(story.Id, new CreateStoryCharacterRequest("Zephyr", null, null));

        Assert.Equal("Unspecified", response!.Kind);
        Assert.Null(response.Species);
        Assert.Null(response.ClothingAndAccessories);
        Assert.Null(response.DistinctiveFeatures);
        Assert.Equal("None", response.BehaviorProfile);
    }

    [Fact]
    public async Task Create_with_an_over_length_field_throws_a_DomainException_and_persists_nothing()
    {
        var (service, repo, story) = Build();

        await Assert.ThrowsAsync<DomainException>(() => service.CreateCharacterAsync(
            story.Id, new CreateStoryCharacterRequest("Zephyr", null, null, Species: new string('x', StoryCharacter.SpeciesMaxLength + 1))));

        Assert.Empty(repo.Characters);
    }

    // ---- Update: null = unchanged, blank = clear ----

    [Fact]
    public async Task Update_with_null_canonical_fields_leaves_them_unchanged()
    {
        var (service, repo, story) = Build();
        var character = Attach(repo, story, StoryCharacter.Create(story.Id, "Zephyr", "d", "v", CharacterBehaviorProfile.None, CharacterKind.Human, "species", "clothes", "features"));

        var response = await service.UpdateCharacterAsync(story.Id, character.Id, new UpdateStoryCharacterRequest("Zephyr", "d2", "v2"));

        Assert.Equal("Human", response!.Kind);
        Assert.Equal("species", response.Species);
        Assert.Equal("clothes", response.ClothingAndAccessories);
        Assert.Equal("features", response.DistinctiveFeatures);
        Assert.Equal("v2", response.VisualDescription); // the ordinary fields still update
    }

    [Fact]
    public async Task Update_with_a_blank_string_clears_only_that_field_and_a_non_blank_string_replaces_it()
    {
        var (service, repo, story) = Build();
        var character = Attach(repo, story, StoryCharacter.Create(story.Id, "Zephyr", "d", "v", CharacterBehaviorProfile.None, CharacterKind.Animal, "species", "clothes", "features"));

        var response = await service.UpdateCharacterAsync(story.Id, character.Id, new UpdateStoryCharacterRequest(
            "Zephyr", "d", "v", CharacterBehaviorProfile.None, Kind: null, Species: "   ", ClothingAndAccessories: "", DistinctiveFeatures: " new features "));

        Assert.Equal("Animal", response!.Kind);
        Assert.Null(response.Species);
        Assert.Null(response.ClothingAndAccessories);
        Assert.Equal("new features", response.DistinctiveFeatures);
    }

    [Fact]
    public async Task Update_can_change_the_kind_and_still_passes_the_requested_behavior_profile_through()
    {
        var (service, repo, story) = Build();
        var character = Attach(repo, story, StoryCharacter.Create(story.Id, "Zephyr", "d", "v", CharacterBehaviorProfile.AnthropomorphicCat));

        var response = await service.UpdateCharacterAsync(story.Id, character.Id, new UpdateStoryCharacterRequest(
            "Zephyr", "d", "v", CharacterBehaviorProfile.AnthropomorphicCat, CharacterKind.AnthropomorphicAnimal));

        Assert.Equal("AnthropomorphicAnimal", response!.Kind);
        Assert.Equal("AnthropomorphicCat", response.BehaviorProfile);
    }

    [Fact]
    public async Task Update_with_an_over_length_field_throws_a_DomainException_before_anything_is_saved()
    {
        var (service, repo, story) = Build();
        var character = Attach(repo, story, StoryCharacter.Create(story.Id, "Zephyr", "d", "v", species: "species"));

        await Assert.ThrowsAsync<DomainException>(() => service.UpdateCharacterAsync(story.Id, character.Id, new UpdateStoryCharacterRequest(
            "Zephyr", "d", "v", ClothingAndAccessories: new string('x', StoryCharacter.ClothingAndAccessoriesMaxLength + 1))));

        Assert.Equal("species", character.Species);
        Assert.Null(character.ClothingAndAccessories);
    }

    // ---- Response fields ----

    [Fact]
    public void Response_exposes_the_reference_workflow_state()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Zephyr", null, "Slate-blue fur", species: "tabby cat");
        character.SetReferenceCandidate("a.png", "p", "gemini", ReferenceImageSource.Generated);
        character.ApproveReferenceImage();
        character.SetReferenceCandidate("b.png", null, "user-upload", ReferenceImageSource.Uploaded);

        var response = StoryCharacterResponse.FromDomain(character);

        Assert.True(response.HasReferenceImage);
        Assert.Equal("Approved", response.ReferenceImageStatus);
        Assert.True(response.HasPendingReferenceImage);
        Assert.Equal("Generated", response.ReferenceImageSource);
        Assert.Equal("Uploaded", response.PendingReferenceImageSource);
        Assert.True(response.CanGenerateReference);
        Assert.Equal(new[] { "clothingAndAccessories", "distinctiveFeatures" }, response.MissingReferenceFields);
    }

    [Fact]
    public void Response_says_a_name_only_character_cannot_generate_and_lists_everything_missing()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Pluma", null, null);

        var response = StoryCharacterResponse.FromDomain(character);

        Assert.False(response.CanGenerateReference);
        Assert.Equal(new[] { "appearance", "species", "clothingAndAccessories", "distinctiveFeatures" }, response.MissingReferenceFields);
        Assert.False(response.HasPendingReferenceImage);
        Assert.Null(response.PendingReferenceImageSource);
        Assert.Equal("Generated", response.ReferenceImageSource);
    }

    [Fact]
    public void Response_serialises_with_the_camelCase_contract_the_frontend_codes_against()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Zephyr", null, "v", CharacterBehaviorProfile.None, CharacterKind.AnthropomorphicAnimal, "cat");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(StoryCharacterResponse.FromDomain(character), Json));
        var root = doc.RootElement;

        Assert.Equal("AnthropomorphicAnimal", root.GetProperty("kind").GetString());
        Assert.Equal("cat", root.GetProperty("species").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("clothingAndAccessories").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("distinctiveFeatures").ValueKind);
        Assert.False(root.GetProperty("hasPendingReferenceImage").GetBoolean());
        Assert.Equal("Generated", root.GetProperty("referenceImageSource").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("pendingReferenceImageSource").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("missingReferenceFields").ValueKind);
        Assert.True(root.GetProperty("canGenerateReference").GetBoolean());
        // Pre-existing fields are still there.
        Assert.Equal("None", root.GetProperty("behaviorProfile").GetString());
        Assert.True(root.TryGetProperty("hasReferenceImage", out _));
    }

    [Fact]
    public void Reference_image_response_serialises_the_new_trailing_fields_and_stays_compatible()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Zephyr", null, "v");
        character.SetReferenceCandidate("a.png", "p", "gemini", ReferenceImageSource.Generated);
        character.ApproveReferenceImage();
        character.SetReferenceCandidate("b.png", null, "user-upload", ReferenceImageSource.Uploaded);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(StoryReferenceImageResponse.FromCharacter(character), Json));
        var root = doc.RootElement;

        Assert.Equal("Approved", root.GetProperty("status").GetString());
        Assert.Equal("a.png", root.GetProperty("imagePath").GetString());
        Assert.True(root.GetProperty("hasPending").GetBoolean());
        Assert.Equal("Generated", root.GetProperty("source").GetString());
        Assert.Equal("Uploaded", root.GetProperty("pendingSource").GetString());

        // The legacy 5-argument construction (and locations) still compile and default the new fields.
        var legacy = new StoryReferenceImageResponse(Guid.NewGuid(), "Pending", null, null, null);
        Assert.False(legacy.HasPending);
        Assert.Null(legacy.Source);
        Assert.Null(legacy.PendingSource);
    }

    [Fact]
    public void Request_bodies_deserialise_the_kind_as_a_string_enum_and_treat_an_omitted_field_as_null()
    {
        var withKind = JsonSerializer.Deserialize<UpdateStoryCharacterRequest>(
            """{"name":"Zephyr","description":null,"visualDescription":"v","behaviorProfile":"AnthropomorphicCat","kind":"Human","species":"","clothingAndAccessories":"x"}""", Json)!;
        var omitted = JsonSerializer.Deserialize<UpdateStoryCharacterRequest>("""{"name":"Zephyr","description":null,"visualDescription":null}""", Json)!;
        var created = JsonSerializer.Deserialize<CreateStoryCharacterRequest>("""{"name":"Zephyr","description":null,"visualDescription":null,"kind":"Other","distinctiveFeatures":"f"}""", Json)!;

        Assert.Equal(CharacterKind.Human, withKind.Kind);
        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, withKind.BehaviorProfile);
        Assert.Equal("", withKind.Species); // blank = clear, distinct from null = unchanged
        Assert.Equal("x", withKind.ClothingAndAccessories);
        Assert.Null(withKind.DistinctiveFeatures);
        Assert.Null(omitted.Kind);
        Assert.Null(omitted.Species);
        Assert.Equal(CharacterKind.Other, created.Kind);
        Assert.Equal("f", created.DistinctiveFeatures);
    }

    // ---- Regression: the project-level (non-Story) reference mapping is untouched ----

    [Fact]
    public void A_Story_seeded_reference_row_is_still_mapped_to_a_scene_by_its_character_name_label()
    {
        var seeded = AssetReference.CreateGenerated(Guid.NewGuid(), AssetReferenceType.Character, "stories/x/ref.png", "p", "gemini", label: "Zephyr");
        seeded.Approve();
        var other = AssetReference.CreateGenerated(Guid.NewGuid(), AssetReferenceType.Character, "stories/x/other.png", "p", "gemini", label: "Bo");
        other.Approve();

        var matched = ReferenceMatcher.Match(
            new[] { seeded, other }, new[] { "zephyr" }, "Zephyr walks in.", r => r.Type, r => r.Label);

        Assert.Same(seeded, Assert.Single(matched));
    }
}
