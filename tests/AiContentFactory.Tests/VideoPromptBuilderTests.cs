using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Storyboards;
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
        var iChar = prompt.IndexOf("consistent with the character reference", System.StringComparison.Ordinal);
        var iEnv = prompt.IndexOf("consistent with the environment reference", System.StringComparison.Ordinal);
        var iCam = prompt.IndexOf("push-in toward the subject", System.StringComparison.Ordinal);
        var iStyle = prompt.IndexOf("Photoreal doc style.", System.StringComparison.Ordinal);
        var iFormat = prompt.IndexOf("Vertical 9:16, about 8 seconds.", System.StringComparison.Ordinal);
        var iRestrict = prompt.IndexOf("No on-screen text", System.StringComparison.Ordinal);

        Assert.True(iAction >= 0 && iChar > iAction && iEnv > iChar && iCam > iEnv && iStyle > iCam && iFormat > iStyle && iRestrict > iFormat,
            $"sentence order wrong in: {prompt}");
    }

    [Fact]
    public void Omits_the_character_sentence_when_there_is_no_character_reference()
    {
        var prompt = VideoPromptBuilder.Build(Spec(character: false));

        Assert.DoesNotContain("character reference", prompt);
        Assert.Contains("consistent with the environment reference", prompt);
    }

    [Fact]
    public void Omits_both_reference_sentences_when_neither_reference_exists()
    {
        var prompt = VideoPromptBuilder.Build(Spec(character: false, environment: false));

        Assert.DoesNotContain("character reference", prompt);
        Assert.DoesNotContain("environment reference", prompt);
        Assert.Contains("push-in toward the subject", prompt);
    }

    [Theory]
    [InlineData(CameraMovement.Static, "Locked-off static shot, no camera movement.")]
    [InlineData(CameraMovement.SlowPushIn, "Slow, steady push-in toward the subject.")]
    [InlineData(CameraMovement.HandheldFollow, "Handheld camera following the subject at a steady distance.")]
    [InlineData(CameraMovement.OverShoulder, "Over-the-shoulder shot framed just behind the subject.")]
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
        Assert.Contains("Slow, subtle push-in toward the subject.", a);
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
        Assert.Contains("Vertical 9:16, about 8 seconds.", VideoPromptBuilder.Build(Spec(duration: 8)));
        Assert.Contains("about 10 seconds.", VideoPromptBuilder.Build(Spec(duration: 45))); // clamped
        Assert.Contains("about 8 seconds.", VideoPromptBuilder.Build(Spec(duration: 0)));   // unset -> 8
    }

    [Fact]
    public void The_paragraph_always_ends_by_forbidding_on_screen_text()
    {
        Assert.EndsWith("No on-screen text, subtitles, captions, logos or watermarks.", VideoPromptBuilder.Build(Spec()));
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
}
