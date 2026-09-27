using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryCharacterCanonicalProfileTests
{
    private static StoryCharacter NewCharacter() =>
        StoryCharacter.Create(Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat");

    private static StoryCharacter NewApprovedCharacter()
    {
        var character = NewCharacter();
        character.MarkReferenceImageGenerated("assets/approved.png", "approved prompt", "gemini");
        character.ApproveReferenceImage();
        return character;
    }

    // ---- Create defaults ----

    [Fact]
    public void Create_without_canonical_args_uses_defaults()
    {
        var character = NewCharacter();

        Assert.Equal(CharacterKind.Unspecified, character.Kind);
        Assert.Null(character.Species);
        Assert.Null(character.ClothingAndAccessories);
        Assert.Null(character.DistinctiveFeatures);
        Assert.Equal(ReferenceImageSource.Generated, character.ReferenceImageSource);
        Assert.Null(character.PendingReferenceImagePath);
        Assert.Null(character.PendingReferenceImageSource);
        Assert.False(character.HasPendingReferenceImage);
    }

    [Fact]
    public void Create_with_canonical_args_trims_and_sets()
    {
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Milo", null, null,
            CharacterBehaviorProfile.AnthropomorphicCat,
            CharacterKind.AnthropomorphicAnimal, "  Scottish Fold  ", " red scarf ", "  ");

        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, character.BehaviorProfile);
        Assert.Equal(CharacterKind.AnthropomorphicAnimal, character.Kind);
        Assert.Equal("Scottish Fold", character.Species);
        Assert.Equal("red scarf", character.ClothingAndAccessories);
        Assert.Null(character.DistinctiveFeatures);
    }

    [Fact]
    public void Create_rejects_over_length_species()
    {
        Assert.Throws<DomainException>(() => StoryCharacter.Create(
            Guid.NewGuid(), "Milo", null, null, species: new string('x', 101)));
    }

    // ---- UpdateCanonicalProfile ----

    [Fact]
    public void UpdateCanonicalProfile_non_blank_values_are_trimmed_and_set()
    {
        var character = NewCharacter();

        character.UpdateCanonicalProfile(CharacterKind.Animal, "  tabby cat ", " blue collar ", " torn left ear ");

        Assert.Equal(CharacterKind.Animal, character.Kind);
        Assert.Equal("tabby cat", character.Species);
        Assert.Equal("blue collar", character.ClothingAndAccessories);
        Assert.Equal("torn left ear", character.DistinctiveFeatures);
    }

    [Fact]
    public void UpdateCanonicalProfile_null_leaves_values_unchanged()
    {
        var character = NewCharacter();
        character.UpdateCanonicalProfile(CharacterKind.Human, "species", "clothes", "features");

        character.UpdateCanonicalProfile(null, null, null, null);

        Assert.Equal(CharacterKind.Human, character.Kind);
        Assert.Equal("species", character.Species);
        Assert.Equal("clothes", character.ClothingAndAccessories);
        Assert.Equal("features", character.DistinctiveFeatures);
    }

    [Fact]
    public void UpdateCanonicalProfile_blank_string_clears_only_that_field()
    {
        var character = NewCharacter();
        character.UpdateCanonicalProfile(CharacterKind.Human, "species", "clothes", "features");

        character.UpdateCanonicalProfile(null, "   ", "", null);

        Assert.Equal(CharacterKind.Human, character.Kind);
        Assert.Null(character.Species);
        Assert.Null(character.ClothingAndAccessories);
        Assert.Equal("features", character.DistinctiveFeatures);
    }

    [Fact]
    public void UpdateCanonicalProfile_does_not_touch_behavior_profile_or_appearance()
    {
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Milo", "desc", "visual", CharacterBehaviorProfile.AnthropomorphicCat);

        character.UpdateCanonicalProfile(CharacterKind.AnthropomorphicAnimal, "cat", null, null);

        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, character.BehaviorProfile);
        Assert.Equal("visual", character.VisualDescription);
        Assert.Equal("desc", character.Description);
    }

    [Theory]
    [InlineData(100, 1000, 1000, true)]
    [InlineData(101, 0, 0, false)]
    [InlineData(0, 1001, 0, false)]
    [InlineData(0, 0, 1001, false)]
    public void UpdateCanonicalProfile_enforces_max_lengths(int speciesLen, int clothingLen, int featuresLen, bool valid)
    {
        var character = NewCharacter();

        string? Make(int length) => length == 0 ? null : new string('a', length);

        if (valid)
        {
            character.UpdateCanonicalProfile(null, Make(speciesLen), Make(clothingLen), Make(featuresLen));
            Assert.Equal(speciesLen, character.Species!.Length);
            Assert.Equal(clothingLen, character.ClothingAndAccessories!.Length);
            Assert.Equal(featuresLen, character.DistinctiveFeatures!.Length);
        }
        else
        {
            Assert.Throws<DomainException>(() =>
                character.UpdateCanonicalProfile(null, Make(speciesLen), Make(clothingLen), Make(featuresLen)));
        }
    }

    [Fact]
    public void UpdateCanonicalProfile_failure_leaves_entity_unchanged()
    {
        var character = NewCharacter();
        character.UpdateCanonicalProfile(CharacterKind.Animal, "species", "clothes", "features");

        Assert.Throws<DomainException>(() =>
            character.UpdateCanonicalProfile(CharacterKind.Human, "new species", new string('x', 1001), "new features"));

        Assert.Equal(CharacterKind.Animal, character.Kind);
        Assert.Equal("species", character.Species);
        Assert.Equal("clothes", character.ClothingAndAccessories);
        Assert.Equal("features", character.DistinctiveFeatures);
    }

    // ---- SetReferenceCandidate ----

    [Fact]
    public void SetReferenceCandidate_when_pending_status_behaves_like_legacy_overwrite()
    {
        var character = NewCharacter();

        character.SetReferenceCandidate("assets/a.png", "prompt a", "gemini", ReferenceImageSource.Generated);

        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);
        Assert.Equal("assets/a.png", character.ReferenceImagePath);
        Assert.Equal("prompt a", character.ReferenceImagePrompt);
        Assert.Equal("gemini", character.ReferenceImageProvider);
        Assert.Equal(ReferenceImageSource.Generated, character.ReferenceImageSource);
        Assert.False(character.HasPendingReferenceImage);
        Assert.Null(character.PendingReferenceImagePath);
    }

    [Fact]
    public void SetReferenceCandidate_when_generated_overwrites_main_fields_and_records_source()
    {
        var character = NewCharacter();
        character.MarkReferenceImageGenerated("assets/old.png", "old prompt", "gemini");

        character.SetReferenceCandidate("assets/upload.png", null, null, ReferenceImageSource.Uploaded);

        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);
        Assert.Equal("assets/upload.png", character.ReferenceImagePath);
        Assert.Null(character.ReferenceImagePrompt);
        Assert.Null(character.ReferenceImageProvider);
        Assert.Equal(ReferenceImageSource.Uploaded, character.ReferenceImageSource);
        Assert.False(character.HasPendingReferenceImage);
    }

    [Fact]
    public void SetReferenceCandidate_when_approved_writes_only_pending_slot()
    {
        var character = NewApprovedCharacter();

        character.SetReferenceCandidate("assets/new.png", "new prompt", "flow", ReferenceImageSource.Uploaded);

        // Approved image untouched.
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("assets/approved.png", character.ReferenceImagePath);
        Assert.Equal("approved prompt", character.ReferenceImagePrompt);
        Assert.Equal("gemini", character.ReferenceImageProvider);
        Assert.Equal(ReferenceImageSource.Generated, character.ReferenceImageSource);

        // Candidate recorded.
        Assert.True(character.HasPendingReferenceImage);
        Assert.Equal("assets/new.png", character.PendingReferenceImagePath);
        Assert.Equal("new prompt", character.PendingReferenceImagePrompt);
        Assert.Equal("flow", character.PendingReferenceImageProvider);
        Assert.Equal(ReferenceImageSource.Uploaded, character.PendingReferenceImageSource);
    }

    [Fact]
    public void SetReferenceCandidate_when_approved_replaces_previous_pending_candidate()
    {
        var character = NewApprovedCharacter();
        character.SetReferenceCandidate("assets/p1.png", "p1", "gemini", ReferenceImageSource.Generated);

        character.SetReferenceCandidate("assets/p2.png", "p2", null, ReferenceImageSource.Uploaded);

        Assert.Equal("assets/p2.png", character.PendingReferenceImagePath);
        Assert.Equal("p2", character.PendingReferenceImagePrompt);
        Assert.Null(character.PendingReferenceImageProvider);
        Assert.Equal(ReferenceImageSource.Uploaded, character.PendingReferenceImageSource);
        Assert.Equal("assets/approved.png", character.ReferenceImagePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SetReferenceCandidate_throws_on_blank_path(string? path)
    {
        var character = NewCharacter();

        Assert.Throws<DomainException>(() =>
            character.SetReferenceCandidate(path!, "p", "gemini", ReferenceImageSource.Generated));
    }

    // ---- PromotePendingReferenceImage ----

    [Fact]
    public void PromotePendingReferenceImage_replaces_approved_image_and_clears_pending()
    {
        var character = NewApprovedCharacter();
        character.SetReferenceCandidate("assets/new.png", "new prompt", "flow", ReferenceImageSource.Uploaded);

        character.PromotePendingReferenceImage();

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("assets/new.png", character.ReferenceImagePath);
        Assert.Equal("new prompt", character.ReferenceImagePrompt);
        Assert.Equal("flow", character.ReferenceImageProvider);
        Assert.Equal(ReferenceImageSource.Uploaded, character.ReferenceImageSource);
        Assert.False(character.HasPendingReferenceImage);
        Assert.Null(character.PendingReferenceImagePath);
        Assert.Null(character.PendingReferenceImagePrompt);
        Assert.Null(character.PendingReferenceImageProvider);
        Assert.Null(character.PendingReferenceImageSource);
    }

    [Fact]
    public void PromotePendingReferenceImage_without_pending_throws()
    {
        var character = NewApprovedCharacter();

        var ex = Assert.Throws<DomainException>(() => character.PromotePendingReferenceImage());

        Assert.Equal("There is no pending reference image to approve.", ex.Message);
        Assert.Equal("assets/approved.png", character.ReferenceImagePath);
    }

    // ---- DiscardPendingReferenceImage ----

    [Fact]
    public void DiscardPendingReferenceImage_clears_pending_and_keeps_approved_image()
    {
        var character = NewApprovedCharacter();
        character.SetReferenceCandidate("assets/new.png", "new prompt", "flow", ReferenceImageSource.Uploaded);

        character.DiscardPendingReferenceImage();

        Assert.False(character.HasPendingReferenceImage);
        Assert.Null(character.PendingReferenceImagePath);
        Assert.Null(character.PendingReferenceImagePrompt);
        Assert.Null(character.PendingReferenceImageProvider);
        Assert.Null(character.PendingReferenceImageSource);
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("assets/approved.png", character.ReferenceImagePath);
    }

    [Fact]
    public async Task DiscardPendingReferenceImage_is_noop_without_pending_and_does_not_touch()
    {
        var character = NewApprovedCharacter();
        var updatedAt = character.UpdatedAt;
        await Task.Delay(5);

        character.DiscardPendingReferenceImage();

        Assert.Equal(updatedAt, character.UpdatedAt);
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
    }

    // ---- Legacy behaviour unchanged ----

    [Fact]
    public void MarkReferenceImageGenerated_still_overwrites_main_fields_even_when_approved()
    {
        var character = NewApprovedCharacter();

        character.MarkReferenceImageGenerated("assets/legacy.png", "legacy prompt", "gemini");

        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);
        Assert.Equal("assets/legacy.png", character.ReferenceImagePath);
        Assert.Equal("legacy prompt", character.ReferenceImagePrompt);
        Assert.False(character.HasPendingReferenceImage);
    }

    [Fact]
    public void ApproveReferenceImage_still_requires_generated_status()
    {
        var character = NewCharacter();
        Assert.Throws<DomainException>(() => character.ApproveReferenceImage());

        character.MarkReferenceImageGenerated("assets/a.png", "p", "gemini");
        character.ApproveReferenceImage();

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
    }

    [Fact]
    public void Update_still_resets_behavior_profile_when_omitted_and_leaves_canonical_profile_alone()
    {
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Milo", "d", "v", CharacterBehaviorProfile.AnthropomorphicCat,
            CharacterKind.AnthropomorphicAnimal, "cat", "scarf", "ear");

        character.Update("Milo2", "d2", "v2");

        Assert.Equal(CharacterBehaviorProfile.None, character.BehaviorProfile);
        Assert.Equal(CharacterKind.AnthropomorphicAnimal, character.Kind);
        Assert.Equal("cat", character.Species);
        Assert.Equal("scarf", character.ClothingAndAccessories);
        Assert.Equal("ear", character.DistinctiveFeatures);
    }
}
