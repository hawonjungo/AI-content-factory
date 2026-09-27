using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="ImagePromptComposer.Compose"/>'s scoped behavior-clause
/// wiring: a clause is only ever added for a name present in
/// <c>characterLabels</c> whose <c>behaviorProfiles</c> entry is
/// non-<see cref="CharacterBehaviorProfile.None"/> AND whose scene action
/// matches a cue (see <see cref="CharacterBehaviorClausesTests"/> for the cue
/// matching itself). Never global, never inferred from name/niche/keywords -
/// <see cref="ImagePromptComposer.Compose"/> has no niche/category parameter
/// at all, so its output is mechanically independent of any such value.
/// </summary>
public class ImagePromptComposerTests
{
    private const string SittingAction = "The character sits upright on the stool and looks out the window";

    private static string Compose(
        string action = SittingAction,
        IReadOnlyList<string>? characterLabels = null,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? behaviorProfiles = null,
        bool hasCharacterReference = true,
        IReadOnlyDictionary<string, string>? visualDescriptions = null) =>
        ImagePromptComposer.Compose(
            action, "photoreal doc style", hasCharacterReference,
            characterLabels ?? Array.Empty<string>(), visualDescriptions, behaviorProfiles);

    // --- 1: opted-in cat + matching action -> clause present ---------------

    [Fact]
    public void Cat_flagged_character_with_a_matching_action_gets_the_behavior_clause()
    {
        var prompt = Compose(
            characterLabels: new[] { "Milo" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat });

        Assert.Contains("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upright", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // --- 2: works generically, not name-based special-casing ---------------

    [Fact]
    public void A_synthetic_non_Milo_name_also_gets_the_clause_proving_no_name_special_casing()
    {
        var prompt = Compose(
            characterLabels: new[] { "Blorptagon" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Blorptagon"] = CharacterBehaviorProfile.AnthropomorphicCat });

        Assert.Contains("Blorptagon", prompt);
        Assert.Contains("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // --- 3: disabled (None) cat character -> byte-identical to no-clause ---

    [Fact]
    public void Cat_character_with_None_profile_produces_byte_identical_output_to_no_behaviorProfiles_at_all()
    {
        var withExplicitNone = Compose(
            characterLabels: new[] { "Milo" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.None });
        var withNoDictionary = Compose(characterLabels: new[] { "Milo" }, behaviorProfiles: null);

        Assert.Equal(withNoDictionary, withExplicitNone);
        Assert.DoesNotContain("anthropomorphically", withExplicitNone, StringComparison.OrdinalIgnoreCase);
    }

    // --- 4: generic/non-cat-named character, not opted in -> no clause -----

    [Fact]
    public void A_dog_named_character_with_None_profile_gets_no_clause()
    {
        var prompt = Compose(
            characterLabels: new[] { "Rex" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Rex"] = CharacterBehaviorProfile.None });

        Assert.DoesNotContain("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cat", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // --- 5: human character, not opted in -> no clause, no animal text -----

    [Fact]
    public void A_human_character_with_None_profile_gets_no_clause_and_no_animal_specific_text()
    {
        var prompt = Compose(
            characterLabels: new[] { "Alex" },
            visualDescriptions: new Dictionary<string, string> { ["Alex"] = "a woman in her 30s with short black hair" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Alex"] = CharacterBehaviorProfile.None });

        Assert.Contains("Alex", prompt);
        Assert.DoesNotContain("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paw", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // --- 6: landscape/no-character scene -> unaffected, no crash -----------

    [Fact]
    public void A_landscape_scene_with_no_characters_is_unaffected_even_with_a_behaviorProfiles_dictionary_present()
    {
        var withDictionary = Compose(
            action: "A wide aerial shot glides over the mountains at sunrise",
            characterLabels: Array.Empty<string>(),
            hasCharacterReference: false,
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat });
        var withoutDictionary = Compose(
            action: "A wide aerial shot glides over the mountains at sunrise",
            characterLabels: Array.Empty<string>(),
            hasCharacterReference: false,
            behaviorProfiles: null);

        Assert.Equal(withoutDictionary, withDictionary);
        Assert.DoesNotContain("anthropomorphically", withDictionary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Feature", withDictionary);
    }

    // --- 7: food-only scene, no character reference at all -----------------

    [Fact]
    public void A_food_only_scene_with_no_character_reference_is_unaffected()
    {
        var prompt = Compose(
            action: "A close-up of coffee being poured into a ceramic mug",
            characterLabels: Array.Empty<string>(),
            hasCharacterReference: false,
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat });

        Assert.DoesNotContain("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Feature", prompt);
        Assert.Contains("coffee being poured", prompt);
    }

    // --- 8: no category/niche concept at all in this composer --------------

    [Fact]
    public void Compose_has_no_niche_or_category_parameter_the_clause_is_governed_only_by_BehaviorProfile()
    {
        // ImagePromptComposer.Compose's signature carries no Niche/category
        // value whatsoever - the same behaviorProfiles input always produces
        // the same clause regardless of any project-level free-text field a
        // caller might otherwise have threaded through (it can't, there's no
        // parameter for it). This is a structural guarantee, not just a
        // runtime one.
        var prompt1 = Compose(
            characterLabels: new[] { "Milo" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat });
        var prompt2 = Compose(
            characterLabels: new[] { "Milo" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat });

        Assert.Equal(prompt1, prompt2);
    }

    // --- 9: mixed scene - only the flagged character's clause appears ------

    [Fact]
    public void Mixed_scene_only_the_flagged_characters_clause_appears_the_other_characters_none_leaks_in()
    {
        var prompt = Compose(
            action: "Milo sits upright on the stool while Rex stands quietly nearby",
            characterLabels: new[] { "Milo", "Rex" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile>
            {
                ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
                ["Rex"] = CharacterBehaviorProfile.None,
            });

        Assert.Contains("Milo", prompt);
        Assert.Contains("Rex", prompt);
        // Exactly one behavior clause instance - not duplicated for Rex.
        var occurrences = System.Text.RegularExpressions.Regex.Matches(prompt, "anthropomorphically").Count;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Mixed_scene_with_two_opted_in_characters_and_a_non_matching_action_neither_gets_a_clause()
    {
        // Both opted in, but the shared scene action has no matching cue at
        // all - CharacterBehaviorClauses.For evaluates the SAME shared
        // action text per name (the source data only ever has one action for
        // the whole scene), so with no cue present neither contributes anything.
        var prompt = Compose(
            action: "Milo and Mimi nap quietly in a sunbeam together",
            characterLabels: new[] { "Milo", "Mimi" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile>
            {
                ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
                ["Mimi"] = CharacterBehaviorProfile.AnthropomorphicCat,
            });

        Assert.DoesNotContain("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Two_opted_in_characters_matching_the_SAME_cue_produce_the_clause_only_once()
    {
        // Milo and Mimi share the same action text (the source data only
        // ever has one action for the whole scene) and both opted in, so the
        // clause CharacterBehaviorClauses.For produces is identical for both
        // - it must appear exactly once in the composed prompt, not twice.
        var prompt = Compose(
            action: "Milo and Mimi walk together, sightseeing along the busy market street",
            characterLabels: new[] { "Milo", "Mimi" },
            behaviorProfiles: new Dictionary<string, CharacterBehaviorProfile>
            {
                ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
                ["Mimi"] = CharacterBehaviorProfile.AnthropomorphicCat,
            });

        var occurrences = System.Text.RegularExpressions.Regex.Matches(prompt, "anthropomorphically").Count;
        Assert.Equal(1, occurrences);
    }
}
