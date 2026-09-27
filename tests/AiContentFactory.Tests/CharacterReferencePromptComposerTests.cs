using System.Text.RegularExpressions;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The composer is pure and deterministic, so every test builds a spec from
/// INVENTED names/species (never the real series' cast) - which also proves no
/// character or species is hardcoded in the generic logic.
/// </summary>
public class CharacterReferencePromptComposerTests
{
    private static CharacterReferenceSpec Spec(
        string name = "Zephyr",
        string? species = "tabby cat",
        CharacterKind kind = CharacterKind.Animal,
        string? appearance = "Slate-blue fur, a white chest patch, round amber eyes",
        string? clothing = null,
        string? features = null,
        bool optIn = false,
        string? look = null,
        string? lookNegative = null) =>
        new(name, species, kind, appearance, clothing, features, optIn, look, lookNegative);

    private static CharacterReferenceSpec AnthropomorphicSpec() => Spec(
        name: "Zephyr", species: "tabby cat", kind: CharacterKind.AnthropomorphicAnimal, optIn: true,
        clothing: "a mustard raincoat and red boots", features: "a notched left ear");

    private static CharacterReferenceSpec HumanSpec() => Spec(
        name: "Rowan Pike", species: null, kind: CharacterKind.Human,
        appearance: "Tall, short silver hair, warm brown eyes, olive skin", clothing: "a green apron", features: "a small scar over the right eyebrow");

    private static CharacterReferenceSpec DogSpec() => Spec(
        name: "Biscuit", species: "dog", kind: CharacterKind.Animal,
        appearance: "Golden short coat, floppy ears, a stubby tail", clothing: null, features: "a white paw on the front left leg");

    private static string Positive(CharacterReferenceSpec spec) =>
        CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.InApp).Prompt;

    private static bool IsNaturalAnimal(CharacterReferenceSpec spec) =>
        spec.Kind == CharacterKind.Animal && !spec.AnthropomorphicOptIn;

    private static bool IsNeutral(CharacterReferenceSpec spec) =>
        !spec.AnthropomorphicOptIn && spec.Kind is CharacterKind.Unspecified or CharacterKind.Other;

    /// <summary>The single pose phrase each branch must carry exactly once.</summary>
    private static string PosePhrase(CharacterReferenceSpec spec) =>
        IsNaturalAnimal(spec) ? "natural, neutral animal stance"
        : IsNeutral(spec) ? "neutral, relaxed standing pose that is natural for its body"
        : "A-pose";

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static bool HasWord(string text, string pattern) =>
        Regex.IsMatch(text, $@"\b(?:{pattern})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IEnumerable<object[]> AllTargets() =>
        Enum.GetValues<ReferencePromptTarget>().Select(t => new object[] { t });

    public static IEnumerable<object[]> RepresentativeSpecs()
    {
        yield return new object[] { AnthropomorphicSpec() };
        yield return new object[] { Spec() };
        yield return new object[] { HumanSpec() };
        yield return new object[] { DogSpec() };
        yield return new object[] { Spec(name: "Quorra", species: "cave golem", kind: CharacterKind.Other) };
        yield return new object[] { Spec(name: "Pluma", species: null, kind: CharacterKind.Unspecified, appearance: null) };
    }

    // ---- Kind scoping ----

    [Fact]
    public void Anthropomorphic_opt_in_uses_upright_on_two_legs_wording_without_human_specific_anatomy()
    {
        var prompt = Positive(AnthropomorphicSpec());

        Assert.Contains("Full-body character reference of Zephyr, an anthropomorphic tabby cat character.", prompt);
        Assert.Contains("The character stands upright on two legs in a neutral, relaxed A-pose", prompt);
        Assert.Equal(1, Count(prompt, "upright on two legs")); // stated once
        Assert.Contains("arms slightly away from the body", prompt);
        Assert.Contains("ears, tail, markings", prompt);
        Assert.False(HasWord(prompt, "fingers?|hands?|palms?|thumbs?|knuckles?|human"), prompt);
    }

    [Fact]
    public void A_normal_animal_gets_natural_stance_wording_and_no_anthropomorphic_words()
    {
        var prompt = Positive(Spec());

        Assert.Contains("Full-body character reference of Zephyr, a tabby cat.", prompt);
        Assert.Contains("natural, neutral animal stance", prompt);
        Assert.Contains("all paws are fully visible and touching the ground", prompt);
        Assert.DoesNotContain("anthropomorphic", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upright", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("two legs", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.False(HasWord(prompt, "arms?|hands?|fingers?|human|crossed"), prompt);
    }

    [Fact]
    public void A_normal_animal_with_a_mythological_name_is_explicitly_kept_an_ordinary_animal()
    {
        var result = CharacterReferencePromptComposer.Compose(Spec(name: "Sekhmet", species: "Egyptian Mau cat"), ReferencePromptTarget.InApp);

        Assert.Contains("a real, ordinary animal with the natural anatomy, proportions and posture of its species", result.Prompt);
        Assert.Contains("not a person-like figure, deity, mascot or costumed character", result.Prompt);
        Assert.Contains("humanoid body", result.NegativePrompt);
        Assert.Contains("standing on hind legs", result.NegativePrompt);
        Assert.DoesNotContain("A-pose", result.Prompt);
    }

    [Fact]
    public void The_person_like_exclusions_are_only_added_for_a_natural_animal()
    {
        foreach (var spec in new[] { AnthropomorphicSpec(), HumanSpec(), Spec(kind: CharacterKind.Other) })
        {
            var result = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.InApp);

            Assert.DoesNotContain("humanoid body", result.NegativePrompt);
            Assert.DoesNotContain("ordinary animal", result.Prompt);
        }
    }

    [Fact]
    public void A_human_gets_an_upright_A_pose_with_arms_away_from_the_body_and_no_animal_or_species_wording()
    {
        var spec = HumanSpec() with { Species = "gargoyle" }; // a stray species must not leak into a human prompt

        var prompt = Positive(spec);

        Assert.Contains("Full-body character reference of Rowan Pike, a human character.", prompt);
        Assert.Contains("stands upright in a neutral, relaxed A-pose", prompt);
        Assert.Contains("arms slightly away from the body", prompt);
        Assert.DoesNotContain("gargoyle", prompt);
        Assert.False(HasWord(prompt, "animals?|paws?|fur|tail|ears|anthropomorphic|species"), prompt);
    }

    [Fact]
    public void A_dog_never_receives_any_cat_specific_text()
    {
        foreach (var target in Enum.GetValues<ReferencePromptTarget>())
        {
            var result = CharacterReferencePromptComposer.Compose(DogSpec(), target);
            var all = result.Prompt + " " + result.NegativePrompt + " " + string.Join(' ', result.Notes);

            Assert.Contains("a dog", result.Prompt);
            Assert.False(HasWord(all, "cats?|kittens?|felines?|whiskers?|meow\\w*|purr\\w*"), all);
            Assert.DoesNotContain("anthropomorphic", all, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(CharacterKind.Other, "cave golem", true)]
    [InlineData(CharacterKind.Unspecified, "cave golem", true)]
    [InlineData(CharacterKind.Other, null, false)]
    [InlineData(CharacterKind.Unspecified, null, false)]
    public void Other_and_Unspecified_kinds_use_neutral_species_agnostic_wording(CharacterKind kind, string? species, bool mentionsSpecies)
    {
        var prompt = Positive(Spec(name: "Quorra", species: species, kind: kind, appearance: "Mossy stone body with two glowing blue eyes"));

        Assert.StartsWith("Full-body character reference of Quorra", prompt);
        Assert.Equal(mentionsSpecies, prompt.Contains("cave golem"));
        Assert.Contains("neutral, relaxed standing pose that is natural for its body", prompt);
        Assert.Contains("without making it more person-like than described", prompt);
        Assert.DoesNotContain("A-pose", prompt);
        Assert.Contains("down to the feet or base", prompt);
        Assert.DoesNotContain("anthropomorphic", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.False(HasWord(prompt, "paws?|human|animals?|cats?|dogs?"), prompt);
        if (species is null)
        {
            Assert.DoesNotContain("who is a", prompt);
        }
    }

    [Theory]
    [InlineData(CharacterKind.Unspecified)]
    [InlineData(CharacterKind.Animal)]
    [InlineData(CharacterKind.AnthropomorphicAnimal)] // the KIND alone, without the resolved flag, is not enough
    [InlineData(CharacterKind.Human)]
    [InlineData(CharacterKind.Other)]
    public void Anthropomorphic_wording_is_impossible_without_the_opt_in_flag(CharacterKind kind)
    {
        foreach (var species in new[] { "cat", "tabby cat", "housecat", "dog", null })
        {
            var prompt = Positive(Spec(species: species, kind: kind, optIn: false));

            Assert.DoesNotContain("anthropomorphic", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("upright on two legs", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("two legs", prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_opt_in_flag_switches_the_wording_on_and_only_the_flag()
    {
        Assert.Contains("anthropomorphic", Positive(Spec(species: "otter", kind: CharacterKind.Animal, optIn: true)));
        Assert.Contains("anthropomorphic animal character", Positive(Spec(species: null, kind: CharacterKind.Unspecified, optIn: true)));
    }

    [Fact]
    public void OptIn_is_derived_from_the_kind_or_the_behavior_profile_never_from_a_species_or_a_name()
    {
        StoryCharacter Build(string species, CharacterKind kind, CharacterBehaviorProfile profile) =>
            StoryCharacter.Create(Guid.NewGuid(), "Zephyr the Cat", "desc", "visual", profile, kind, species);

        Assert.True(CharacterReferenceSpec.FromCharacter(Build("otter", CharacterKind.AnthropomorphicAnimal, CharacterBehaviorProfile.None), null).AnthropomorphicOptIn);
        Assert.True(CharacterReferenceSpec.FromCharacter(Build("otter", CharacterKind.Unspecified, CharacterBehaviorProfile.AnthropomorphicCat), null).AnthropomorphicOptIn);
        Assert.False(CharacterReferenceSpec.FromCharacter(Build("cat", CharacterKind.Animal, CharacterBehaviorProfile.None), null).AnthropomorphicOptIn);
        Assert.False(CharacterReferenceSpec.FromCharacter(Build("cat", CharacterKind.Unspecified, CharacterBehaviorProfile.None), null).AnthropomorphicOptIn);
    }

    [Theory]
    [InlineData(CharacterKind.Human)]
    [InlineData(CharacterKind.Animal)]
    [InlineData(CharacterKind.Other)]
    public void An_explicit_Kind_wins_over_the_cat_behavior_profile_so_no_anthropomorphic_wording_is_produced(CharacterKind kind)
    {
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Zephyr", "desc", "Slate-blue fur", CharacterBehaviorProfile.AnthropomorphicCat, kind, kind == CharacterKind.Human ? null : "tabby cat");

        var spec = CharacterReferenceSpec.FromCharacter(character, null);
        var prompt = Positive(spec);

        Assert.False(spec.AnthropomorphicOptIn);
        Assert.DoesNotContain("anthropomorphic", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("two legs", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Older_data_with_an_Unspecified_kind_and_the_cat_profile_still_gets_the_anthropomorphic_wording()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Zephyr", "desc", "Slate-blue fur", CharacterBehaviorProfile.AnthropomorphicCat);

        var prompt = Positive(CharacterReferenceSpec.FromCharacter(character, null));

        Assert.Contains("anthropomorphic animal character", prompt);
        Assert.Contains("stands upright on two legs", prompt);
    }

    [Fact]
    public void FromCharacter_maps_the_canonical_fields_and_the_style_and_uses_the_VisualDescription_only()
    {
        var character = StoryCharacter.Create(
            Guid.NewGuid(), "Nyx", "the role summary", "glossy black feathers", CharacterBehaviorProfile.None,
            CharacterKind.Animal, "raven", "a silver ring", "one white feather");
        var style = PresetCatalog.FindStyle("anime")!;

        var spec = CharacterReferenceSpec.FromCharacter(character, style);

        Assert.Equal("Nyx", spec.Name);
        Assert.Equal("glossy black feathers", spec.Appearance); // never the role/personality Description
        Assert.Equal("raven", spec.Species);
        Assert.Equal("a silver ring", spec.ClothingAndAccessories);
        Assert.Equal("one white feather", spec.DistinctiveFeatures);
        Assert.Equal(style.ReferenceLookGuidance, spec.SeriesLook);
        Assert.Equal(style.ReferenceNegativePrompt, spec.SeriesLookNegative);
        Assert.Null(CharacterReferenceSpec.FromCharacter(character, null).SeriesLook);
    }

    [Fact]
    public void A_role_description_is_never_used_as_the_canonical_appearance()
    {
        var character = StoryCharacter.Create(Guid.NewGuid(), "Nyx", "A brave, sarcastic detective.", null, species: "raven");

        var spec = CharacterReferenceSpec.FromCharacter(character, null);
        var prompt = Positive(spec);

        Assert.Null(spec.Appearance);
        Assert.DoesNotContain("sarcastic", prompt);
        Assert.DoesNotContain("Canonical appearance", prompt);
        Assert.Contains("appearance", CharacterReferencePromptComposer.GetMissingFields(spec));
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("  ", " ", false)]
    [InlineData("Slate fur", null, true)]
    [InlineData(null, "raven", true)]
    public void HasEnoughToGenerate_needs_an_appearance_or_a_species(string? appearance, string? species, bool expected)
    {
        Assert.Equal(expected, Spec(appearance: appearance, species: species, kind: CharacterKind.Unspecified).HasEnoughToGenerate);
    }

    [Theory]
    [InlineData(CharacterKind.Human, null, "stale species", false)] // a Human prompt ignores the species, so it cannot open the paid gate
    [InlineData(CharacterKind.Human, "Tall, silver hair", "stale species", true)]
    [InlineData(CharacterKind.Animal, null, "raven", true)]
    [InlineData(CharacterKind.AnthropomorphicAnimal, null, "raven", true)]
    [InlineData(CharacterKind.Other, null, "cave golem", true)]
    [InlineData(CharacterKind.Unspecified, null, "cave golem", true)]
    [InlineData(CharacterKind.Animal, null, null, false)]
    public void The_paid_gate_counts_a_species_only_when_it_is_actually_used(CharacterKind kind, string? appearance, string? species, bool expected)
    {
        Assert.Equal(expected, Spec(kind: kind, appearance: appearance, species: species).HasEnoughToGenerate);
    }

    // ---- Missing information ----

    [Fact]
    public void Without_clothing_the_prompt_says_nothing_is_added_beyond_the_description()
    {
        var result = CharacterReferencePromptComposer.Compose(Spec(clothing: null), ReferencePromptTarget.InApp);

        Assert.Contains("Nothing is added beyond the description: no extra clothing, accessories or items.", result.Prompt);
        Assert.DoesNotContain("Do not add clothing", result.Prompt);
        Assert.Contains(CharacterReferencePromptComposer.FieldClothingAndAccessories, result.MissingFields);
    }

    [Fact]
    public void With_clothing_it_is_listed_exactly_as_given_and_no_other_items_may_be_added()
    {
        var result = CharacterReferencePromptComposer.Compose(AnthropomorphicSpec(), ReferencePromptTarget.InApp);

        Assert.Contains("Clothing and accessories, exactly as listed (including any shoes and signature items): a mustard raincoat and red boots.", result.Prompt);
        Assert.Contains("All of them are clearly visible.", result.Prompt);
        Assert.Contains("Do not add clothing, accessories or items that are not listed.", result.Prompt);
        Assert.DoesNotContain("Nothing is added beyond the description", result.Prompt);
        Assert.DoesNotContain(CharacterReferencePromptComposer.FieldClothingAndAccessories, result.MissingFields);
        Assert.Contains("Distinctive features, exactly as described: a notched left ear.", result.Prompt);
    }

    [Fact]
    public void A_character_with_nothing_but_a_name_still_composes_and_reports_every_canonical_field_missing()
    {
        var result = CharacterReferencePromptComposer.Compose(
            Spec(name: "Pluma", species: null, kind: CharacterKind.Unspecified, appearance: null), ReferencePromptTarget.Generic);

        Assert.Equal(
            new[] { "appearance", "species", "clothingAndAccessories", "distinctiveFeatures" },
            result.MissingFields);
        Assert.Contains("Pluma", result.Prompt);
        Assert.DoesNotContain("Canonical appearance", result.Prompt);
        Assert.DoesNotContain("Distinctive features", result.Prompt);
        Assert.Contains("Seamless solid white background", result.Prompt); // the standard staging is still there
    }

    [Fact]
    public void A_fully_described_character_has_no_missing_fields_and_species_is_not_required_for_a_human()
    {
        Assert.Empty(CharacterReferencePromptComposer.Compose(AnthropomorphicSpec() with { Appearance = "x" }, ReferencePromptTarget.InApp).MissingFields);
        Assert.Equal(
            new[] { "appearance", "clothingAndAccessories", "distinctiveFeatures" },
            CharacterReferencePromptComposer.GetMissingFields(Spec(kind: CharacterKind.Human, species: null, appearance: null)));
    }

    // ---- Standard staging ----

    [Theory]
    [MemberData(nameof(RepresentativeSpecs))]
    public void Every_kind_gets_the_standard_framing_pose_face_background_and_lighting_wording(CharacterReferenceSpec spec)
    {
        var prompt = Positive(spec);

        Assert.StartsWith("Full-body character reference of ", prompt);
        Assert.Contains("fully visible and touching the ground", prompt);
        Assert.Contains("centered", prompt);
        Assert.Contains("space around it", prompt);
        Assert.Contains("nothing cropped", prompt);
        // Only an upright character gets a human-style A-pose; a natural animal or an unspecified body keeps a natural stance.
        Assert.Contains(PosePhrase(spec), prompt);
        Assert.Contains("facing straight toward the camera at flat, straight-on eye level", prompt);
        Assert.Contains("looks directly at the camera with a neutral expression", prompt);
        Assert.Contains("the eyes are clearly visible and the face is unobstructed", prompt);
        Assert.Contains("no exaggerated emotion and no extreme head rotation", prompt);
        Assert.Contains("not sitting, crouching", prompt);
        Assert.Contains("no dynamic pose, extreme perspective, gestures or interaction with objects", prompt);
        Assert.Contains("Soft, even studio lighting with minimal harsh shadows; no dramatic coloured light and no strong rim light.", prompt);
        Assert.Contains("High detail, sharp features, clean studio render.", prompt);
        Assert.Contains("clearly visible and unobstructed", prompt);
    }

    [Theory]
    [MemberData(nameof(RepresentativeSpecs))]
    public void The_background_is_seamless_solid_white_and_never_any_other_background(CharacterReferenceSpec spec)
    {
        var prompt = Positive(spec);

        Assert.Contains("Seamless solid white background: plain, clean and uninterrupted, with no scenery, furniture, props, food, patterns, text, logos or watermark, so the character is easy to isolate.", prompt);
        Assert.False(HasWord(prompt, "gr[ae]y|gradient|studio backdrop|black|beige|cream|forest|garden|street|city|sky|beach|sunset|kitchen|bedroom"), prompt);
    }

    [Theory]
    [MemberData(nameof(RepresentativeSpecs))]
    public void Quality_wording_is_practical_only_never_8K_or_similar(CharacterReferenceSpec spec)
    {
        var prompt = Positive(spec);

        Assert.DoesNotContain("8K", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4K", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.False(HasWord(prompt, "masterpiece|ultra|hyper-?realistic|award"), prompt);
    }

    [Theory]
    [MemberData(nameof(RepresentativeSpecs))]
    public void Each_standard_statement_appears_exactly_once(CharacterReferenceSpec spec)
    {
        var prompt = Positive(spec);

        var phrases = new List<string> { "white background", "eye level", "neutral expression", "Soft, even studio lighting", "nothing cropped", "Full-body character reference", "touching the ground" };
        phrases.Add(PosePhrase(spec));
        foreach (var phrase in phrases)
        {
            Assert.True(Count(prompt, phrase) == 1, $"'{phrase}' appears {Count(prompt, phrase)} times in: {prompt}");
        }
    }

    // ---- Pronouns and story content ----

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void No_gendered_pronouns_are_ever_used_even_for_gendered_looking_names(ReferencePromptTarget target)
    {
        foreach (var spec in new[]
                 {
                     Spec(name: "Rowan"), Spec(name: "Pluma", kind: CharacterKind.Human, species: null), AnthropomorphicSpec() with { Name = "Sir Bartholomew" },
                     HumanSpec(), DogSpec() with { Name = "Lady Gwendolyn" }
                 })
        {
            var result = CharacterReferencePromptComposer.Compose(spec, target);
            var text = result.Prompt + " " + result.NegativePrompt + " " + string.Join(' ', result.Notes);

            Assert.False(HasWord(text, "he|she|his|her|hers|him|himself|herself"), text);
        }
    }

    [Theory]
    [MemberData(nameof(RepresentativeSpecs))]
    public void No_dialogue_scene_action_video_motion_or_camera_movement_is_ever_included(CharacterReferenceSpec spec)
    {
        foreach (var target in Enum.GetValues<ReferencePromptTarget>())
        {
            var result = CharacterReferencePromptComposer.Compose(spec, target);
            var text = result.Prompt + " " + result.NegativePrompt;

            Assert.False(
                HasWord(text, "dialogue|says?|said|speaks?|speaking|talks?|talking|pans?|zoom\\w*|dolly|tracking|motion|animat\\w*|walk\\w*|run\\w*|jump\\w*|video|scene|action|cinematic|tilt\\w*|orbit\\w*"),
                text);
        }
    }

    [Fact]
    public void No_character_or_species_is_hardcoded_a_different_name_never_yields_the_original_cast()
    {
        var names = new[] { "Zephyr", "Kestrel Voss", "Ottoline", "Bo" };
        foreach (var name in names)
        {
            foreach (var target in Enum.GetValues<ReferencePromptTarget>())
            {
                var text = CharacterReferencePromptComposer.Compose(Spec(name: name, species: "river otter"), target).Prompt;

                Assert.Contains(name, text);
                Assert.DoesNotContain("Milo", text);
                Assert.DoesNotContain("Mimi", text);
                Assert.DoesNotContain("cat", text.Replace("indicat", "").Replace("dedicat", ""), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void User_text_is_embedded_as_a_single_clean_line_and_cannot_inject_midjourney_parameters()
    {
        var spec = Spec(appearance: "Slate fur,\n  round eyes --ar 16:9 --no white background.  ", clothing: "  a red scarf; ");

        var mj = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.Midjourney).Prompt;

        Assert.DoesNotContain("\n", mj);
        Assert.DoesNotContain("  ", mj);
        Assert.Equal(1, Count(mj, "--ar"));
        Assert.Equal(1, Count(mj, "--no"));
        Assert.Contains("Canonical appearance: Slate fur, round eyes -ar 16:9 -no white background.", mj);
        Assert.Contains("exactly as listed (including any shoes and signature items): a red scarf.", mj);
    }

    // ---- Series style ----

    [Fact]
    public void The_series_look_appears_exactly_once_as_the_look_only_line_when_provided()
    {
        var prompt = Positive(Spec(look: "hand-painted watercolor look, soft paper texture."));

        Assert.Contains("Rendering style (look only): hand-painted watercolor look, soft paper texture.", prompt);
        Assert.Equal(1, Count(prompt, "watercolor"));
        Assert.Equal(1, Count(prompt, "Rendering style"));
        Assert.DoesNotContain("..", prompt);
        Assert.EndsWith("Rendering style (look only): hand-painted watercolor look, soft paper texture.", prompt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_series_look_there_is_no_style_line_at_all(string? look)
    {
        var prompt = Positive(Spec(look: look));

        Assert.DoesNotContain("Rendering style", prompt);
        Assert.DoesNotContain("Series visual style", prompt);
        Assert.False(HasWord(prompt, "style|tone|mood|aesthetic"), prompt);
    }

    [Fact]
    public void The_series_look_negative_goes_into_the_negative_list_once_and_the_look_itself_never_does()
    {
        var spec = Spec(look: "clay stop-frame look", lookNegative: "photorealistic, text, glossy plastic");

        var result = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.InApp);
        var terms = result.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries);

        Assert.Single(terms, t => t == "photorealistic");
        Assert.Single(terms, t => t == "glossy plastic");
        Assert.Single(terms, t => t == "text"); // overlaps with the shared list, de-duplicated
        Assert.Equal(terms.Length, terms.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("clay stop-frame", result.NegativePrompt);
        // ...and the look is not restated anywhere in the positive text either.
        Assert.Equal(1, Count(result.Prompt, "clay stop-frame"));
        Assert.DoesNotContain("photorealistic", result.Prompt);
    }

    [Fact]
    public void The_negative_list_is_the_shared_character_sheet_terms_including_the_pose_and_composition_exclusions()
    {
        // A non-animal kind: a natural animal additionally gets the person-like exclusions (tested separately).
        var result = CharacterReferencePromptComposer.Compose(Spec(kind: CharacterKind.Other), ReferencePromptTarget.InApp);
        var terms = result.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries);

        foreach (var expected in new[]
                 {
                     "cropped", "cut off", "sitting", "crouching", "crossed arms", "leaning", "dynamic pose", "extreme perspective",
                     "side view", "gradient background", "patterned background", "food", "furniture", "background scenery",
                     "props", "other characters", "blurry", "watermark"
                 })
        {
            Assert.Contains(expected, terms);
        }

        Assert.Equal(
            AiContentFactory.Application.Agents.AssetReferencePromptAgent.MergeWithQualityNegative(
                AiContentFactory.Domain.AssetReferences.AssetReferenceType.Character, null, singleSubject: true),
            result.NegativePrompt); // one source of truth for negative merging
    }

    // ---- Targets ----

    [Fact]
    public void InApp_returns_the_positive_prompt_and_a_separate_negative_prompt()
    {
        var result = CharacterReferencePromptComposer.Compose(AnthropomorphicSpec(), ReferencePromptTarget.InApp);

        Assert.False(string.IsNullOrWhiteSpace(result.NegativePrompt));
        Assert.DoesNotContain("Avoid:", result.Prompt);
        Assert.DoesNotContain("--ar", result.Prompt);
        Assert.DoesNotContain("cropped, cut off", result.Prompt); // exclusions live only in the negative field
    }

    [Theory]
    [InlineData(ReferencePromptTarget.Generic)]
    [InlineData(ReferencePromptTarget.Flow)]
    [InlineData(ReferencePromptTarget.Dalle)]
    [InlineData(ReferencePromptTarget.Flux)]
    public void Text_only_targets_fold_the_exclusions_into_one_self_contained_text_with_no_negative_field(ReferencePromptTarget target)
    {
        var spec = AnthropomorphicSpec() with { SeriesLook = "clay look", SeriesLookNegative = "glossy plastic" };
        var inApp = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.InApp);

        var result = CharacterReferencePromptComposer.Compose(spec, target);

        Assert.Null(result.NegativePrompt);
        Assert.Equal($"{inApp.Prompt} Avoid: {inApp.NegativePrompt}.", result.Prompt);
        Assert.Contains("cropped", result.Prompt);
        Assert.Contains("glossy plastic", result.Prompt);
        Assert.DoesNotContain("--ar", result.Prompt);
        Assert.DoesNotContain("--no", result.Prompt);
    }

    [Fact]
    public void Midjourney_ends_with_ar_2_3_and_a_comma_separated_no_list_and_has_no_folded_avoid_sentence()
    {
        var spec = AnthropomorphicSpec() with { SeriesLook = "clay look", SeriesLookNegative = "glossy plastic" };
        var inApp = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.InApp);

        var result = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.Midjourney);

        Assert.Null(result.NegativePrompt);
        Assert.Equal($"{inApp.Prompt} --ar 2:3 --no {inApp.NegativePrompt}", result.Prompt);
        Assert.DoesNotContain("Avoid:", result.Prompt);
        Assert.Matches(@"--ar 2:3 --no [^-]+$", result.Prompt);
        Assert.Contains("cropped, cut off, sitting", result.Prompt);
        Assert.Equal(1, Count(result.Prompt, "--no"));
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Every_target_carries_the_common_honest_note_about_consistency(ReferencePromptTarget target)
    {
        var notes = CharacterReferencePromptComposer.Compose(Spec(), target).Notes;

        Assert.Contains(notes, n => n.Contains("không đảm bảo") && n.Contains("ba việc khác nhau"));
        Assert.True(notes.Count >= 2);
        Assert.All(notes, n => Assert.True(n.Length < 320, n));
    }

    [Fact]
    public void Per_target_notes_are_specific_and_do_not_claim_a_negative_field_that_does_not_exist()
    {
        string Notes(ReferencePromptTarget t) => string.Join(' ', CharacterReferencePromptComposer.Compose(Spec(), t).Notes);

        Assert.Contains("ingredient", Notes(ReferencePromptTarget.Flow));
        Assert.Contains("gộp vào văn bản", Notes(ReferencePromptTarget.Flow));
        Assert.Contains("--ar 2:3", Notes(ReferencePromptTarget.Midjourney));
        Assert.Contains("kiểm tra lại", Notes(ReferencePromptTarget.Midjourney));
        Assert.Contains("không có ô negative prompt", Notes(ReferencePromptTarget.Dalle));
        Assert.Contains("không có ô negative prompt", Notes(ReferencePromptTarget.Flux));
        Assert.Contains("gộp vào cuối prompt", Notes(ReferencePromptTarget.Generic));
        Assert.DoesNotContain("ingredient", Notes(ReferencePromptTarget.Dalle));
    }

    [Theory]
    [MemberData(nameof(RepresentativeSpecs))]
    public void The_prompt_never_says_a_T_pose_is_acceptable_only_the_A_pose_is_requested(CharacterReferenceSpec spec)
    {
        var prompt = Positive(spec);

        Assert.DoesNotContain("T-pose", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acceptable", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clean_studio_render_is_only_added_when_no_series_look_defines_the_medium()
    {
        var withoutLook = Positive(Spec(look: null));
        var withLook = Positive(Spec(look: "hand-painted watercolor look"));

        Assert.Contains("High detail, sharp features, clean studio render.", withoutLook);
        Assert.DoesNotContain("studio render", withLook);
        Assert.Contains("High detail, sharp features.", withLook);
        Assert.EndsWith("Rendering style (look only): hand-painted watercolor look.", withLook);
    }

    private static string[] ExclusionTerms(CharacterReferencePrompt result, ReferencePromptTarget target)
    {
        var list = target switch
        {
            ReferencePromptTarget.InApp => result.NegativePrompt!,
            ReferencePromptTarget.Midjourney => result.Prompt[(result.Prompt.IndexOf("--no ", StringComparison.Ordinal) + 5)..],
            _ => result.Prompt[(result.Prompt.IndexOf(" Avoid: ", StringComparison.Ordinal) + 8)..].TrimEnd('.')
        };
        return list.Split(',', StringSplitOptions.TrimEntries);
    }

    [Theory]
    [InlineData("a red scarf", null)]
    [InlineData(null, "a scar over the left eye")]
    [InlineData("a red scarf", "a scar over the left eye")]
    public void Props_is_dropped_from_the_exclusions_when_the_character_has_signature_items_but_other_characters_stays(string? clothing, string? features)
    {
        foreach (var target in Enum.GetValues<ReferencePromptTarget>())
        {
            var terms = ExclusionTerms(CharacterReferencePromptComposer.Compose(Spec(clothing: clothing, features: features), target), target);

            Assert.DoesNotContain("props", terms);
            Assert.Contains("other characters", terms);
        }
    }

    [Fact]
    public void Props_stays_in_the_exclusions_when_there_are_no_signature_items()
    {
        foreach (var target in Enum.GetValues<ReferencePromptTarget>())
        {
            var terms = ExclusionTerms(CharacterReferencePromptComposer.Compose(Spec(clothing: null, features: null), target), target);

            Assert.Contains("props", terms);
            Assert.Contains("other characters", terms);
        }
    }

    [Fact]
    public void Compose_is_deterministic()
    {
        foreach (var target in Enum.GetValues<ReferencePromptTarget>())
        {
            var first = CharacterReferencePromptComposer.Compose(AnthropomorphicSpec(), target);
            var second = CharacterReferencePromptComposer.Compose(AnthropomorphicSpec(), target);

            Assert.Equal(first.Prompt, second.Prompt);
            Assert.Equal(first.NegativePrompt, second.NegativePrompt);
            Assert.Equal(first.MissingFields, second.MissingFields);
            Assert.Equal(first.Notes, second.Notes);
        }
    }

    // ---- Target parsing ----

    [Theory]
    [InlineData("inapp", ReferencePromptTarget.InApp)]
    [InlineData("InApp", ReferencePromptTarget.InApp)]
    [InlineData("GENERIC", ReferencePromptTarget.Generic)]
    [InlineData("flow", ReferencePromptTarget.Flow)]
    [InlineData("Midjourney", ReferencePromptTarget.Midjourney)]
    [InlineData(" dalle ", ReferencePromptTarget.Dalle)]
    [InlineData("FLUX", ReferencePromptTarget.Flux)]
    public void TryParseTarget_accepts_every_target_case_insensitively(string value, ReferencePromptTarget expected)
    {
        Assert.True(CharacterReferencePromptComposer.TryParseTarget(value, out var target));
        Assert.Equal(expected, target);
        Assert.Equal(expected.ToString().ToLowerInvariant(), CharacterReferencePromptComposer.TargetId(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("sora")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("midjourney,dalle")]
    [InlineData("generic,flow")]
    public void TryParseTarget_rejects_blank_numeric_and_unknown_values(string? value)
    {
        Assert.False(CharacterReferencePromptComposer.TryParseTarget(value, out _));
    }
}
