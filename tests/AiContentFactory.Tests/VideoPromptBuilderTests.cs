using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The deterministic Scene-data -> final-video-prompt step: one clean English
/// paragraph, directly pasteable into Google Flow - one specific action, exactly
/// one camera move, optional reference-consistency sentences, project style,
/// ~8s vertical, and no headings / labels / separators / metadata.
/// </summary>
public class VideoPromptBuilderTests
{
    private static VideoPromptSpec Spec(
        string? action = "The man picks up the cup with his right hand and takes a sip",
        CameraMovement camera = CameraMovement.SlowPushIn,
        bool character = true,
        bool environment = true,
        string? style = "photoreal doc style",
        int duration = 8) =>
        new(action, camera, character, environment, style, duration, "9:16");

    [Fact]
    public void Output_is_one_clean_paragraph_with_no_headings_labels_or_separators()
    {
        var prompt = VideoPromptBuilder.Build(Spec());

        Assert.DoesNotContain("\n", prompt);
        Assert.DoesNotContain("\r", prompt);
        foreach (var marker in new[] { "Action:", "Character:", "Environment:", "Camera:", "Style:", "Format:", "Restrictions:", "---", "###", "•", "- " })
        {
            Assert.DoesNotContain(marker, prompt);
        }
        // Reads as prose: starts with a capital, ends with a full stop.
        Assert.Matches(@"^[A-Z].*\.$", prompt);
    }

    [Fact]
    public void The_paragraph_carries_action_then_references_then_camera_then_style_then_format_then_restriction()
    {
        var prompt = VideoPromptBuilder.Build(Spec());

        var iAction = prompt.IndexOf("picks up the cup", System.StringComparison.Ordinal);
        var iChar = prompt.IndexOf("Consistent character reference", System.StringComparison.Ordinal);
        var iEnv = prompt.IndexOf("Consistent environment reference", System.StringComparison.Ordinal);
        var iCam = prompt.IndexOf("Slow subtle push-in", System.StringComparison.Ordinal);
        var iStyle = prompt.IndexOf("Photoreal doc style.", System.StringComparison.Ordinal);
        var iFormat = prompt.IndexOf("Vertical 9:16, 8s.", System.StringComparison.Ordinal);
        var iRestrict = prompt.IndexOf("No text, logos, watermarks.", System.StringComparison.Ordinal);

        Assert.True(iAction >= 0 && iChar > iAction && iEnv > iChar && iCam > iEnv && iStyle > iCam && iFormat > iStyle && iRestrict > iFormat,
            $"sentence order wrong in: {prompt}");
    }

    [Fact]
    public void Omits_the_character_sentence_when_there_is_no_character_reference()
    {
        var prompt = VideoPromptBuilder.Build(Spec(character: false));

        Assert.DoesNotContain("character reference", prompt);
        Assert.Contains("Consistent environment reference", prompt);
    }

    [Fact]
    public void Omits_both_reference_sentences_when_neither_reference_exists()
    {
        var prompt = VideoPromptBuilder.Build(Spec(character: false, environment: false));

        Assert.DoesNotContain("character reference", prompt);
        Assert.DoesNotContain("environment reference", prompt);
        Assert.Contains("Slow subtle push-in", prompt);
    }

    [Theory]
    [InlineData(CameraMovement.Static, "Static shot, no camera movement.")]
    [InlineData(CameraMovement.SlowPushIn, "Slow subtle push-in.")]
    [InlineData(CameraMovement.HandheldFollow, "Handheld follow at a steady distance.")]
    [InlineData(CameraMovement.OverShoulder, "Over-the-shoulder framing.")]
    public void Camera_is_exactly_one_explicit_movement_never_alternatives(CameraMovement camera, string expected)
    {
        var prompt = VideoPromptBuilder.Build(Spec(camera: camera));

        Assert.Contains(expected, prompt);
        Assert.DoesNotContain("push-in or handheld drift", prompt);
        Assert.DoesNotContain("drift", prompt);
    }

    [Fact]
    public void Unspecified_camera_falls_back_to_one_deterministic_default_sentence()
    {
        var a = VideoPromptBuilder.Build(Spec(camera: CameraMovement.Unspecified));
        var b = VideoPromptBuilder.Build(Spec(camera: CameraMovement.Unspecified));

        Assert.Equal(a, b);
        Assert.Contains("Slow subtle push-in.", a);
    }

    [Fact]
    public void A_multi_action_description_is_reduced_to_one_primary_action()
    {
        var prompt = VideoPromptBuilder.Build(Spec(
            action: "The man walks into the room, then picks up a book, then reads it, then leaves"));

        Assert.StartsWith("The man walks into the room. ", prompt);
        Assert.DoesNotContain("then", prompt);
    }

    [Fact]
    public void A_blank_action_becomes_a_concrete_not_vague_fallback()
    {
        var prompt = VideoPromptBuilder.Build(Spec(action: "   "));

        Assert.StartsWith(VideoPromptBuilder.DefaultAction + ". ", prompt);
        Assert.DoesNotContain("performs a clear action matching the scene", prompt);
        Assert.DoesNotContain("does something", prompt);
    }

    [Fact]
    public void Style_reuses_the_project_style_and_falls_back_to_the_house_default()
    {
        Assert.Contains("Photoreal doc style.", VideoPromptBuilder.Build(Spec(style: "photoreal doc style")));
        Assert.Contains(VideoPromptBuilder.DefaultStyleGuidance + ".", VideoPromptBuilder.Build(Spec(style: null)));
    }

    [Fact]
    public void Format_is_vertical_and_clamped_to_a_single_clip_length()
    {
        Assert.Contains("Vertical 9:16, 8s.", VideoPromptBuilder.Build(Spec(duration: 8)));
        Assert.Contains("Vertical 9:16, 10s.", VideoPromptBuilder.Build(Spec(duration: 45))); // clamped
        Assert.Contains("Vertical 9:16, 8s.", VideoPromptBuilder.Build(Spec(duration: 0)));   // unset -> 8
    }

    [Fact]
    public void The_paragraph_always_ends_by_forbidding_on_screen_text()
    {
        Assert.EndsWith("No text, logos, watermarks.", VideoPromptBuilder.Build(Spec()));
    }

    [Fact]
    public void Same_input_always_produces_the_same_prompt()
    {
        Assert.Equal(VideoPromptBuilder.Build(Spec()), VideoPromptBuilder.Build(Spec()));
    }

    [Fact]
    public void No_vietnamese_or_accented_text_leaks_into_the_prompt()
    {
        // Vietnamese precomposed letters live in Latin-1 Supplement / Latin
        // Extended-A/B / Latin Extended Additional - none of those should appear.
        Assert.DoesNotMatch(@"[À-ɏḀ-ỿ]", VideoPromptBuilder.Build(Spec()));
    }

    [Theory]
    [InlineData("slow push in", CameraMovement.SlowPushIn)]
    [InlineData("dolly out slowly", CameraMovement.SlowPullOut)]
    [InlineData("handheld", CameraMovement.HandheldFollow)]
    [InlineData("locked off", CameraMovement.Static)]
    [InlineData("SlowPushIn", CameraMovement.SlowPushIn)]
    [InlineData("over the shoulder", CameraMovement.OverShoulder)]
    [InlineData("something unrecognised", CameraMovement.Unspecified)]
    public void Legacy_free_text_camera_notes_map_onto_the_enum(string freeText, CameraMovement expected)
    {
        Assert.Equal(expected, VideoPromptBuilder.ParseCamera(freeText));
    }

    // --- Named character / location content (direct Veo flowing-paragraph path) ---

    [Fact]
    public void Named_characters_are_named_in_the_character_consistency_sentence()
    {
        var spec = Spec() with { CharacterLabels = new[] { "Milo", "Mimi" } };

        var prompt = VideoPromptBuilder.Build(spec);

        Assert.Contains(
            "Consistent character reference for Milo and Mimi: same face, body, hair, clothing, outfit as shown in the reference images.",
            prompt);
    }

    [Fact]
    public void No_names_known_falls_back_to_todays_exact_generic_character_sentence()
    {
        // Backward-compat regression: a spec with the old "has reference"
        // signal true but no CharacterLabels (every non-Story project) must
        // produce today's exact generic sentence, unchanged.
        var prompt = VideoPromptBuilder.Build(Spec());

        Assert.Contains("Consistent character reference: same face, body, hair, clothing.", prompt);
    }

    [Fact]
    public void A_location_label_adds_a_setting_sentence_after_the_action_and_before_character_consistency()
    {
        var spec = Spec() with { LocationLabel = "Hanoi Old Quarter" };

        var prompt = VideoPromptBuilder.Build(spec);

        var iAction = prompt.IndexOf("picks up the cup", StringComparison.Ordinal);
        var iSetting = prompt.IndexOf("Setting: Hanoi Old Quarter.", StringComparison.Ordinal);
        var iChar = prompt.IndexOf("Consistent character reference", StringComparison.Ordinal);

        Assert.True(iAction >= 0 && iSetting > iAction && iChar > iSetting, $"order wrong in: {prompt}");
    }

    [Fact]
    public void No_location_known_produces_no_setting_sentence()
    {
        var prompt = VideoPromptBuilder.Build(Spec());

        Assert.DoesNotContain("Setting:", prompt);
    }

    [Fact]
    public void No_name_no_location_produces_the_exact_same_output_as_before_this_feature_existed()
    {
        // Zero-behavior-change proof for every non-Story project: same exact
        // string this method produced before CharacterLabels/LocationLabel existed.
        var prompt = VideoPromptBuilder.Build(Spec());

        Assert.Equal(
            "The man picks up the cup with his right hand and takes a sip. " +
            "Consistent character reference: same face, body, hair, clothing. " +
            "Consistent environment reference: same location, lighting, mood. " +
            "Slow subtle push-in. " +
            "Photoreal doc style. " +
            "Vertical 9:16, 8s. " +
            "No text, logos, watermarks.",
            prompt);
    }

    // --- Flowing-paragraph Google Flow export format (BuildNarrative) ---
    //
    // Replaces the earlier bracketed [VISUAL ACTION]/[LOCATION]/
    // [CHARACTER REF]/[NOTE]/[CAMERA & STYLE] block format (see git history)
    // after hands-on testing showed Google Flow's model reads natural prose
    // better than labelled/bracketed text.

    [Fact]
    public void BuildNarrative_two_known_characters_produces_one_flowing_paragraph_with_no_brackets()
    {
        var spec = Spec() with
        {
            CharacterLabels = new[] { "Milo", "Mimi" },
            LocationLabel = "Hanoi Old Quarter",
            CharacterVisualDescriptions = new Dictionary<string, string>
            {
                ["Milo"] = "a fluffy orange tabby kitten",
                ["Mimi"] = "a white kitten with soft gray patches",
            },
        };

        var prompt = VideoPromptBuilder.BuildNarrative(spec);

        Assert.DoesNotContain("[", prompt);
        Assert.DoesNotContain("]", prompt);
        Assert.Matches(@"^[A-Z].*\.$", prompt);

        // Both names appear near their trait descriptions.
        Assert.Contains("Milo, a fluffy orange tabby kitten", prompt);
        Assert.Contains("Mimi, a white kitten with soft gray patches", prompt);

        // Short inline reference tags, not a verbose "use the attached
        // reference image for exact likeness" instruction sentence.
        Assert.Contains("(refer to the attached Milo reference)", prompt);
        Assert.Contains("(refer to the attached Mimi reference)", prompt);
        Assert.DoesNotContain("use the attached reference image for exact likeness", prompt);

        // Location folded in naturally, not a separate "Setting:"/[LOCATION] line.
        Assert.Contains("Hanoi Old Quarter", prompt);
        Assert.DoesNotContain("Setting:", prompt);

        // Camera phrasing reused from CameraToText, natural cinematic style/closing.
        Assert.Contains("Slow subtle push-in", prompt);
        Assert.EndsWith("No text, subtitles, logos, or watermarks.", prompt);

        // No leftover meta-instruction/NOTE line from the earlier format.
        Assert.DoesNotContain("[NOTE]", prompt);
        Assert.DoesNotContain("definitive visual source", prompt);
    }

    [Fact]
    public void BuildNarrative_a_single_known_character_produces_a_natural_single_character_paragraph()
    {
        var spec = Spec() with
        {
            CharacterLabels = new[] { "Milo" },
            CharacterVisualDescriptions = new Dictionary<string, string> { ["Milo"] = "orange tabby, one white paw, green eyes" },
        };

        var prompt = VideoPromptBuilder.BuildNarrative(spec);

        Assert.DoesNotContain("[", prompt);
        Assert.DoesNotContain("]", prompt);
        Assert.Contains("Milo, orange tabby, one white paw, green eyes (refer to the attached Milo reference)", prompt);

        // No two-character template artifact (no "and" joining a second name, no stray "Mimi"-shaped slot).
        Assert.DoesNotContain(" and Milo", prompt);
        Assert.DoesNotContain("two characters", prompt);
        Assert.EndsWith("No text, subtitles, logos, or watermarks.", prompt);
    }

    [Fact]
    public void BuildNarrative_with_no_known_characters_or_location_still_produces_a_clean_sensible_paragraph()
    {
        // The normal non-Story project case: no CharacterLabels, no LocationLabel.
        var prompt = VideoPromptBuilder.BuildNarrative(Spec());

        Assert.False(string.IsNullOrWhiteSpace(prompt));
        Assert.DoesNotContain("[", prompt);
        Assert.DoesNotContain("]", prompt);
        Assert.DoesNotContain("()", prompt);
        Assert.Contains("picks up the cup", prompt);
        Assert.Contains("Slow subtle push-in", prompt);
        Assert.EndsWith("No text, subtitles, logos, or watermarks.", prompt);
    }

    [Fact]
    public void BuildNarrative_falls_back_to_the_bare_name_when_no_visual_description_is_known()
    {
        var spec = Spec() with { CharacterLabels = new[] { "Milo" }, LocationLabel = "Hanoi Old Quarter" };

        var prompt = VideoPromptBuilder.BuildNarrative(spec);

        Assert.Contains("Milo (refer to the attached Milo reference)", prompt);
        Assert.Contains("Hanoi Old Quarter", prompt);
    }

    [Fact]
    public void BuildNarrative_summarizes_a_long_sentence_like_visual_description_into_a_short_trait_tag()
    {
        var spec = Spec() with
        {
            CharacterLabels = new[] { "Milo" },
            CharacterVisualDescriptions = new Dictionary<string, string>
            {
                ["Milo"] = "A fluffy orange tabby cat with bright green eyes, a white chest patch, "
                    + "and a slightly chubby belly from all the snacking.",
            },
        };

        var prompt = VideoPromptBuilder.BuildNarrative(spec);

        Assert.Contains("(refer to the attached Milo reference)", prompt);
        Assert.DoesNotContain("slightly chubby belly from all the snacking", prompt);
    }

    [Fact]
    public void BuildNarrative_includes_a_consistent_character_appearance_clause_when_a_character_reference_is_in_play()
    {
        var withCharacter = VideoPromptBuilder.BuildNarrative(Spec() with { CharacterLabels = new[] { "Milo" } });
        var withoutCharacter = VideoPromptBuilder.BuildNarrative(Spec(character: false) with { CharacterLabels = Array.Empty<string>() });

        Assert.Contains("consistent character appearance throughout the shot", withCharacter);
        Assert.DoesNotContain("consistent character appearance", withoutCharacter);
    }

    [Fact]
    public void BuildNarrative_is_deterministic_same_input_always_produces_the_same_prompt()
    {
        var spec = Spec() with { CharacterLabels = new[] { "Milo", "Mimi" }, LocationLabel = "Hanoi Old Quarter" };

        Assert.Equal(VideoPromptBuilder.BuildNarrative(spec), VideoPromptBuilder.BuildNarrative(spec));
    }

    [Fact]
    public void Build_direct_Veo_path_ignores_visual_descriptions_and_is_unchanged()
    {
        // CharacterVisualDescriptions/LocationVisualDescription are
        // BuildNarrative-only - Build() (the direct Veo path, which already
        // attaches the real reference images) must be byte-identical whether
        // or not they are set.
        var spec = Spec() with
        {
            CharacterLabels = new[] { "Milo" },
            LocationLabel = "Hanoi Old Quarter",
            CharacterVisualDescriptions = new Dictionary<string, string> { ["Milo"] = "orange tabby" },
            LocationVisualDescription = "narrow streets",
        };

        var withDescriptions = VideoPromptBuilder.Build(spec);
        var withoutDescriptions = VideoPromptBuilder.Build(spec with { CharacterVisualDescriptions = null, LocationVisualDescription = null });

        Assert.Equal(withoutDescriptions, withDescriptions);
        Assert.DoesNotContain("orange tabby", withDescriptions);
        Assert.DoesNotContain("narrow streets", withDescriptions);
    }

    // --- CharacterBehaviorProfiles: scoped anthropomorphic-cat clause ------
    //
    // Never global, never keyword-inferred: a clause is added only for a
    // name present in CharacterLabels whose CharacterBehaviorProfiles entry
    // is non-None AND whose scene Action text matches a cue (see
    // CharacterBehaviorClausesTests for the cue matching itself). Both Build
    // (direct Veo path) and BuildNarrative (Google Flow export) honour it.

    private const string SittingAction = "The character sits upright on the stool and looks out the window";

    [Fact]
    public void Build_adds_the_clause_for_a_cat_flagged_character_with_a_matching_action()
    {
        var spec = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Milo" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat },
        };

        var prompt = VideoPromptBuilder.Build(spec);

        Assert.Contains("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upright", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNarrative_adds_the_clause_for_a_cat_flagged_character_with_a_matching_action()
    {
        var spec = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Milo" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat },
        };

        var prompt = VideoPromptBuilder.BuildNarrative(spec);

        Assert.Contains("anthropomorphically", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upright", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_generic_synthetic_character_name_also_gets_the_clause_proving_no_name_special_casing()
    {
        // Uses a fictitious name that is neither Milo nor Mimi, to prove
        // there's no hardcoded name check anywhere in this path.
        var spec = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Blorptagon" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Blorptagon"] = CharacterBehaviorProfile.AnthropomorphicCat },
        };

        var buildPrompt = VideoPromptBuilder.Build(spec);
        var narrativePrompt = VideoPromptBuilder.BuildNarrative(spec);

        Assert.Contains("anthropomorphically", buildPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anthropomorphically", narrativePrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_and_BuildNarrative_add_no_clause_for_a_cat_character_with_the_disabled_None_profile()
    {
        var flagged = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Milo" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.None },
        };
        var noProfiles = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Milo" },
            CharacterBehaviorProfiles = null,
        };

        // Byte-identical to today's existing behavior for that character.
        Assert.Equal(VideoPromptBuilder.Build(noProfiles), VideoPromptBuilder.Build(flagged));
        Assert.Equal(VideoPromptBuilder.BuildNarrative(noProfiles), VideoPromptBuilder.BuildNarrative(flagged));
        Assert.DoesNotContain("anthropomorphically", VideoPromptBuilder.Build(flagged), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_dog_named_character_with_None_profile_gets_no_clause()
    {
        // No species field exists - the point is proving "not opted in =>
        // nothing added" regardless of what the name implies.
        var spec = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Rex" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Rex"] = CharacterBehaviorProfile.None },
        };

        Assert.DoesNotContain("anthropomorphically", VideoPromptBuilder.Build(spec), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anthropomorphically", VideoPromptBuilder.BuildNarrative(spec), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_human_character_with_None_profile_gets_no_clause_and_no_animal_specific_text()
    {
        var spec = Spec(action: SittingAction) with
        {
            CharacterLabels = new[] { "Alex" },
            CharacterVisualDescriptions = new Dictionary<string, string> { ["Alex"] = "a woman in her 30s with short black hair" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Alex"] = CharacterBehaviorProfile.None },
        };

        var narrative = VideoPromptBuilder.BuildNarrative(spec);

        Assert.Contains("Alex", narrative);
        Assert.DoesNotContain("anthropomorphically", narrative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paw", narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_landscape_scene_with_no_characters_is_unaffected_by_a_present_but_irrelevant_behaviorProfiles_dictionary()
    {
        var spec = Spec(action: "Aerial drone shot gliding over the mountains at sunrise", character: false, environment: false) with
        {
            CharacterLabels = Array.Empty<string>(),
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat },
        };
        var withoutDictionary = spec with { CharacterBehaviorProfiles = null };

        Assert.Equal(VideoPromptBuilder.Build(withoutDictionary), VideoPromptBuilder.Build(spec));
        Assert.Equal(VideoPromptBuilder.BuildNarrative(withoutDictionary), VideoPromptBuilder.BuildNarrative(spec));
        Assert.DoesNotContain("anthropomorphically", VideoPromptBuilder.Build(spec), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_food_only_scene_with_no_character_reference_is_unaffected()
    {
        var spec = Spec(action: "A close-up of coffee being poured into a ceramic mug", character: false, environment: false) with
        {
            CharacterLabels = Array.Empty<string>(),
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat },
        };

        var build = VideoPromptBuilder.Build(spec);
        var narrative = VideoPromptBuilder.BuildNarrative(spec);

        Assert.DoesNotContain("anthropomorphically", build, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anthropomorphically", narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mixed_scene_only_the_flagged_characters_clause_appears_never_the_unflagged_characters()
    {
        var spec = Spec(action: "Milo sits upright on the stool while Rex stands quietly nearby") with
        {
            CharacterLabels = new[] { "Milo", "Rex" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile>
            {
                ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
                ["Rex"] = CharacterBehaviorProfile.None,
            },
        };

        var build = VideoPromptBuilder.Build(spec);
        var narrative = VideoPromptBuilder.BuildNarrative(spec);

        foreach (var prompt in new[] { build, narrative })
        {
            Assert.Contains("Milo", prompt);
            Assert.Contains("Rex", prompt);
            var occurrences = System.Text.RegularExpressions.Regex.Matches(prompt, "anthropomorphically").Count;
            Assert.Equal(1, occurrences); // exactly Milo's clause, never a second one attributable to Rex
        }
    }

    [Fact]
    public void Two_opted_in_characters_matching_the_SAME_cue_produce_the_clause_only_once()
    {
        // Milo and Mimi share the same scene Action text (the source data
        // only ever has one action for the whole scene) and both opted in,
        // so CharacterBehaviorClauses.For produces the identical clause for
        // both - it must appear exactly once, not once per character.
        var spec = Spec(action: "Milo and Mimi walk together, sightseeing along the busy market street") with
        {
            CharacterLabels = new[] { "Milo", "Mimi" },
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile>
            {
                ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
                ["Mimi"] = CharacterBehaviorProfile.AnthropomorphicCat,
            },
        };

        foreach (var prompt in new[] { VideoPromptBuilder.Build(spec), VideoPromptBuilder.BuildNarrative(spec) })
        {
            var occurrences = System.Text.RegularExpressions.Regex.Matches(prompt, "anthropomorphically").Count;
            Assert.Equal(1, occurrences);
        }
    }

    [Fact]
    public void No_characterLabels_and_null_behaviorProfiles_is_byte_identical_to_before_this_feature_existed()
    {
        // Regression proof: the CharacterBehaviorProfiles-unaware call sites
        // (every pre-feature test above) still produce exactly the same
        // output now that the optional param exists.
        Assert.Equal(
            "The man picks up the cup with his right hand and takes a sip. " +
            "Consistent character reference: same face, body, hair, clothing. " +
            "Consistent environment reference: same location, lighting, mood. " +
            "Slow subtle push-in. " +
            "Photoreal doc style. " +
            "Vertical 9:16, 8s. " +
            "No text, logos, watermarks.",
            VideoPromptBuilder.Build(Spec()));
    }
}
