using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Direct unit coverage of <see cref="CharacterBehaviorClauses.For"/>: the
/// deterministic cue-matching core the whole scoped anthropomorphic-cat
/// feature is built on. <see cref="CharacterBehaviorProfile.None"/> (the
/// default for every character that never opted in) must always produce
/// null, regardless of what the action text says - this is the type's own
/// "never global, never inferred" guarantee, independent of any caller.
/// </summary>
public class CharacterBehaviorClausesTests
{
    [Theory]
    [InlineData("The cat sits upright on the stool and holds a tiny cup")]
    [InlineData("The character walks through a doorway")]
    [InlineData("")]
    [InlineData(null)]
    public void None_profile_always_returns_null_regardless_of_action_text(string? action)
    {
        Assert.Null(CharacterBehaviorClauses.For(CharacterBehaviorProfile.None, action));
    }

    [Fact]
    public void AnthropomorphicCat_with_an_unrelated_action_returns_null()
    {
        var clause = CharacterBehaviorClauses.For(
            CharacterBehaviorProfile.AnthropomorphicCat, "The cat naps quietly in a sunbeam by the window");

        Assert.Null(clause);
    }

    [Fact]
    public void AnthropomorphicCat_with_a_blank_action_returns_null()
    {
        Assert.Null(CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, ""));
        Assert.Null(CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, null));
        Assert.Null(CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, "   "));
    }

    [Theory]
    [InlineData("The cat sits upright on the stool and watches the door")]
    [InlineData("It perches on the chair like a tiny person")]
    [InlineData("She remains seated the whole time")]
    public void AnthropomorphicCat_with_a_sitting_cue_produces_a_non_null_clause_mentioning_upright(string action)
    {
        var clause = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, action);

        Assert.NotNull(clause);
        Assert.Contains("upright", clause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cat", clause, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("The cat holds a tiny teacup with both paws")]
    [InlineData("It stirs the soup carefully")]
    [InlineData("The cat picks up the pencil and starts writing")]
    public void AnthropomorphicCat_with_a_handling_cue_produces_a_non_null_clause_mentioning_paw(string action)
    {
        var clause = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, action);

        Assert.NotNull(clause);
        Assert.Contains("paw", clause, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("The cat walks down the busy market street, taking in the sights")]
    [InlineData("It strolls along the riverside promenade")]
    [InlineData("The cat stands at the overlook, admiring the view")]
    [InlineData("They wander through the old town exploring every alley")]
    public void AnthropomorphicCat_with_an_upright_mobility_cue_produces_a_non_null_clause_mentioning_upright(string action)
    {
        var clause = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, action);

        Assert.NotNull(clause);
        Assert.Contains("upright", clause, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("The cat poses for a photo in front of the temple")]
    [InlineData("It waves at the camera with one paw")]
    [InlineData("The cat wears a tiny backpack while sightseeing")]
    [InlineData("She smiles and points at the boats in the harbor")]
    public void AnthropomorphicCat_with_a_posing_cue_produces_a_non_null_clause_mentioning_tourist(string action)
    {
        var clause = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, action);

        Assert.NotNull(clause);
        Assert.Contains("tourist", clause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnthropomorphicCat_with_both_a_sitting_and_handling_cue_produces_the_combined_clause()
    {
        var clause = CharacterBehaviorClauses.For(
            CharacterBehaviorProfile.AnthropomorphicCat, "The cat sits upright and holds a tiny cup of tea");

        Assert.NotNull(clause);
        Assert.Contains("upright", clause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paw", clause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grip", clause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnthropomorphicCat_clause_always_reaffirms_the_subject_remains_a_recognizable_ordinary_cat()
    {
        // The feature's own safety net against runaway "cartoonification" -
        // every clause this method can produce still anchors back to
        // "photorealistic cat", never a fully humanized/cartoon character.
        var sitting = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, "the cat sits upright");
        var handling = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, "the cat holds a cup");

        Assert.Contains("photorealistic cat", sitting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photorealistic cat", handling, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cue_matching_is_case_insensitive()
    {
        var clause = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, "The Cat SITS Upright on the stool");

        Assert.NotNull(clause);
    }

    [Fact]
    public void Same_input_always_produces_the_same_output()
    {
        var a = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, "the cat sits upright");
        var b = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, "the cat sits upright");

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("The cat visits the neighbor's garden")] // contains "sit" inside "visit"
    [InlineData("The cat pads quietly through the household")] // contains "hold" inside "household"
    [InlineData("The cat naps because the afternoon is warm")] // contains "use" inside "because"
    [InlineData("The camera reveals a great view of the yard")] // contains "eat" inside "great"
    [InlineData("The cat studies an old prototype toy on the shelf")] // contains "type" inside "prototype"
    [InlineData("The cat rests on the boardwalk at sunset")] // contains "walk" inside "boardwalk"
    [InlineData("Understanding the map takes the cat a moment")] // contains "stand" inside "understanding"
    [InlineData("The cat is curled up, its fur exposed to the sun")] // contains "pose" inside "exposed"
    [InlineData("The tourist crowd startles the cat")] // contains "tour" inside "tourist"
    [InlineData("The cat seems to swear off naps today")] // contains "wear" inside "swear"
    public void AnthropomorphicCat_does_not_false_positive_on_cues_embedded_inside_unrelated_words(string action)
    {
        var clause = CharacterBehaviorClauses.For(CharacterBehaviorProfile.AnthropomorphicCat, action);

        Assert.Null(clause);
    }
}
