using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryCharacterAndLocationReferenceImageTests
{
    [Fact]
    public void StoryCharacter_MarkReferenceImageGenerated_sets_fields_and_status()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat");

        character.MarkReferenceImageGenerated("assets/milo.png", "grey cat portrait", "gemini");

        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);
        Assert.Equal("assets/milo.png", character.ReferenceImagePath);
        Assert.Equal("grey cat portrait", character.ReferenceImagePrompt);
        Assert.Equal("gemini", character.ReferenceImageProvider);
    }

    [Fact]
    public void StoryCharacter_ApproveReferenceImage_throws_when_not_generated()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat");

        Assert.Throws<DomainException>(() => character.ApproveReferenceImage());
    }

    [Fact]
    public void StoryCharacter_ApproveReferenceImage_succeeds_after_generation()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat");
        character.MarkReferenceImageGenerated("assets/milo.png", "grey cat portrait", "gemini");

        character.ApproveReferenceImage();

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
    }

    [Fact]
    public void StoryLocation_MarkReferenceImageGenerated_sets_fields_and_status()
    {
        var location = StoryLocation.Create(Guid.NewGuid(), "The Attic", "A cluttered storage space.", "dusty wooden beams");

        location.MarkReferenceImageGenerated("assets/attic.png", "dusty attic wide shot", "gemini");

        Assert.Equal(AssetReferenceStatus.Generated, location.ReferenceImageStatus);
        Assert.Equal("assets/attic.png", location.ReferenceImagePath);
        Assert.Equal("dusty attic wide shot", location.ReferenceImagePrompt);
        Assert.Equal("gemini", location.ReferenceImageProvider);
    }

    [Fact]
    public void StoryLocation_ApproveReferenceImage_throws_when_not_generated()
    {
        var location = StoryLocation.Create(Guid.NewGuid(), "The Attic", "A cluttered storage space.", "dusty wooden beams");

        Assert.Throws<DomainException>(() => location.ApproveReferenceImage());
    }

    [Fact]
    public void StoryLocation_ApproveReferenceImage_succeeds_after_generation()
    {
        var location = StoryLocation.Create(Guid.NewGuid(), "The Attic", "A cluttered storage space.", "dusty wooden beams");
        location.MarkReferenceImageGenerated("assets/attic.png", "dusty attic wide shot", "gemini");

        location.ApproveReferenceImage();

        Assert.Equal(AssetReferenceStatus.Approved, location.ReferenceImageStatus);
    }

    // --- BehaviorProfile: explicit per-character opt-in, never inferred ----

    [Fact]
    public void StoryCharacter_Create_defaults_BehaviorProfile_to_None_when_not_specified()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat");

        Assert.Equal(CharacterBehaviorProfile.None, character.BehaviorProfile);
    }

    [Fact]
    public void StoryCharacter_Create_persists_an_explicitly_opted_in_BehaviorProfile()
    {
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat", CharacterBehaviorProfile.AnthropomorphicCat);

        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, character.BehaviorProfile);
    }

    [Fact]
    public void StoryCharacter_Update_can_change_BehaviorProfile_in_either_direction()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat");
        Assert.Equal(CharacterBehaviorProfile.None, character.BehaviorProfile);

        character.Update("Milo", "A curious housecat.", "grey short-haired cat", CharacterBehaviorProfile.AnthropomorphicCat);
        Assert.Equal(CharacterBehaviorProfile.AnthropomorphicCat, character.BehaviorProfile);

        character.Update("Milo", "A curious housecat.", "grey short-haired cat", CharacterBehaviorProfile.None);
        Assert.Equal(CharacterBehaviorProfile.None, character.BehaviorProfile);
    }

    [Fact]
    public void StoryCharacter_Update_defaults_BehaviorProfile_back_to_None_when_the_trailing_param_is_omitted()
    {
        // Regression: a caller that never learned about the new trailing
        // param must not accidentally retain a previously opted-in profile
        // (the parameter's own default is None, matching Create's default).
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Milo", "A curious housecat.", "grey short-haired cat", CharacterBehaviorProfile.AnthropomorphicCat);

        character.Update("Milo", "updated description", "grey short-haired cat");

        Assert.Equal(CharacterBehaviorProfile.None, character.BehaviorProfile);
    }
}
