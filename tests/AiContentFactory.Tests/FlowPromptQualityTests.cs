using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The Flow prompt follows Veo's recommended shape (cinematography, subject +
/// action, context, style, audio), never contradicts its own camera, and only
/// attaches/names a character when the shot actually shows one. No real AI:
/// the prompt agent runs against a stub LLM and services use fakes.
/// </summary>
public class FlowPromptQualityTests
{
    private const string HandheldStyle =
        "photorealistic documentary footage, natural daylight, handheld camera feel, neutral color grade, 50mm lens";

    private static VideoPromptSpec Spec(
        CameraMovement camera = CameraMovement.SlowPushIn,
        ShotSize shot = ShotSize.Unspecified,
        bool hasCharacterReference = false,
        string? style = HandheldStyle) =>
        new(
            Action: "A swarm of brown rats scurries across a dusty temple floor through slanting amber sunbeams.",
            Camera: camera,
            HasCharacterReference: hasCharacterReference,
            HasEnvironmentReference: false,
            StyleGuidance: style,
            DurationSeconds: 8,
            AspectRatio: "9:16",
            Shot: shot);

    // ---- Builder ----

    [Fact]
    public void The_framing_leads_the_prompt_followed_by_the_single_camera_move()
    {
        var prompt = VideoPromptBuilder.BuildNarrative(Spec(CameraMovement.SideTracking, ShotSize.Wide));

        Assert.StartsWith("Wide shot, smooth lateral tracking shot. A swarm of brown rats", prompt);
    }

    [Fact]
    public void A_static_camera_with_framing_reads_naturally()
    {
        Assert.Equal("Close-up, static camera with no movement.", VideoPromptBuilder.CinematographySentence(ShotSize.CloseUp, CameraMovement.Static));
    }

    [Fact]
    public void Without_framing_the_camera_sentence_is_unchanged_from_before()
    {
        var prompt = VideoPromptBuilder.BuildNarrative(Spec());

        Assert.StartsWith("Slow subtle push-in. A swarm of brown rats", prompt);
        Assert.DoesNotContain("A cinematic vertical video of", prompt);
    }

    [Theory]
    [InlineData(CameraMovement.Static)]
    [InlineData(CameraMovement.SlowPushIn)]
    [InlineData(CameraMovement.SideTracking)]
    public void A_style_that_names_camera_motion_never_contradicts_the_scene_camera(CameraMovement camera)
    {
        var prompt = VideoPromptBuilder.BuildNarrative(Spec(camera, ShotSize.Medium));

        Assert.DoesNotContain("handheld", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("natural daylight", prompt);
        Assert.Contains("neutral color grade, 50mm lens", prompt);
    }

    [Fact]
    public void Removing_camera_motion_keeps_look_words_and_falls_back_to_default_when_nothing_is_left()
    {
        Assert.Equal("natural daylight, 50mm lens", VideoPromptBuilder.RemoveCameraMotionWording("natural daylight, handheld camera feel, 50mm lens"));
        Assert.Null(VideoPromptBuilder.RemoveCameraMotionWording("handheld, shaky cam"));

        var prompt = VideoPromptBuilder.BuildNarrative(Spec(style: "handheld, shaky cam"));
        Assert.Contains(VideoPromptBuilder.DefaultStyleGuidance, prompt);
    }

    [Fact]
    public void A_long_prose_preset_loses_only_its_camera_motion_clause_never_a_neighbouring_sentence()
    {
        var style = AiContentFactory.Application.Presets.PresetCatalog.ResolveStyle("cat-travel-stylized-realism").VisualStyleGuidance;

        var cleaned = VideoPromptBuilder.RemoveCameraMotionWording(style)!;

        Assert.DoesNotContain("handheld", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photorealistic as if shot on a 50mm lens.", cleaned);
        Assert.Contains("The cat naturally stands and walks upright on its hind legs, using its front paws dexterously like human hands (holding a camera, gripping a scooter/e-bike throttle, waving hello, holding food).", cleaned);
        Assert.Contains("Always wearing travel accessories (a backwards baseball cap or a beret, a small crossbody bag).", cleaned);
        Assert.Contains("like a viral Facebook/TikTok pet video", cleaned);
    }

    [Fact]
    public void The_documentary_preset_loses_only_handheld()
    {
        var style = AiContentFactory.Application.Presets.PresetCatalog.ResolveStyle("photoreal-doc").VisualStyleGuidance;

        Assert.Equal(
            "photorealistic documentary footage, natural daylight, realistic skin texture and materials, neutral color grade, 50mm lens",
            VideoPromptBuilder.RemoveCameraMotionWording(style));
    }

    [Fact]
    public void A_style_without_motion_wording_is_returned_verbatim()
    {
        const string style = "hand-painted watercolor look; soft pastel palette. Gentle paper texture";

        Assert.Same(style, VideoPromptBuilder.RemoveCameraMotionWording(style));
    }

    [Fact]
    public void A_character_action_ending_in_an_exclamation_never_gets_a_second_stop()
    {
        var prompt = VideoPromptBuilder.BuildNarrative(Spec() with { Action = "Milo leaps onto the boat!", CharacterLabels = new[] { "Milo" } });

        Assert.Contains("leaps onto the boat!", prompt);
        Assert.DoesNotContain("!.", prompt);
    }

    [Fact]
    public void The_prompt_asks_for_ambient_sound_only_so_the_clip_never_invents_speech_or_music()
    {
        var prompt = VideoPromptBuilder.BuildNarrative(Spec());

        Assert.Contains(VideoPromptBuilder.AmbientAudioSentence, prompt);
        Assert.EndsWith("No text, subtitles, logos, or watermarks.", prompt);
    }

    [Fact]
    public void A_scene_without_a_character_reference_never_asks_for_character_consistency()
    {
        Assert.DoesNotContain("consistent character appearance", VideoPromptBuilder.BuildNarrative(Spec(hasCharacterReference: false)));
        Assert.Contains("consistent character appearance", VideoPromptBuilder.BuildNarrative(Spec(hasCharacterReference: true)));
    }

    [Fact]
    public void A_known_location_is_written_as_prose_not_a_label()
    {
        var prompt = VideoPromptBuilder.BuildNarrative(Spec() with { LocationLabel = "Karnak Temple", LocationVisualDescription = "towering sandstone columns" });

        Assert.Contains("The scene takes place at Karnak Temple, towering sandstone columns.", prompt);
        Assert.DoesNotContain("Setting:", prompt);
    }

    [Fact]
    public void The_direct_Veo_prompt_also_gets_the_framing_when_known()
    {
        Assert.Contains("Medium close-up, slow subtle push-in.", VideoPromptBuilder.Build(Spec(shot: ShotSize.MediumCloseUp)));
        Assert.Contains("Slow subtle push-in.", VideoPromptBuilder.Build(Spec()));
    }

    [Fact]
    public void The_image_prompt_never_ends_the_action_with_a_double_period()
    {
        var prompt = ImagePromptComposer.Compose("A cat sits on a pedestal.", "natural light", false, Array.Empty<string>(), null);

        Assert.DoesNotContain("..", prompt);
        Assert.Contains("A cat sits on a pedestal. Visual style", prompt);
    }

    // ---- Prompt agent (stub LLM, no real call) ----

    [Fact]
    public async Task The_agent_parses_framing_visible_names_and_the_on_screen_flag()
    {
        var llm = new StubLlmProvider("""
            {"action":"A golden cat steps into a sunbeam.","shot":"CloseUp","camera":"SideTracking",
             "visibleReferences":["bastet","Nobody"],"characterOnScreen":true,"negativePrompt":null,"visualStyle":null}
            """);
        var agent = new PromptAgent(llm);

        var output = await agent.GenerateAsync(new PromptAgentInput(
            "Enter the African wildcat.", "", "", SceneVisualType.AiVideo, "natural light",
            ReferenceNames: new[] { "Bastet", "Karnak Temple" }, PreviousShot: "Medium, SlowPushIn"));

        Assert.Equal(ShotSize.CloseUp, output.Shot);
        Assert.Equal(CameraMovement.SideTracking, output.Camera);
        Assert.Equal(new[] { "Bastet" }, output.VisibleReferenceNames); // canonical casing, invented name dropped
        Assert.True(output.CharacterOnScreen);
        Assert.Contains("Bastet, Karnak Temple", llm.LastUserPrompt);
        Assert.Contains("Medium, SlowPushIn", llm.LastUserPrompt);
    }

    [Fact]
    public async Task An_older_style_answer_without_the_new_fields_still_parses_and_decides_nothing()
    {
        var agent = new PromptAgent(new StubLlmProvider("""{"action":"A cat walks.","camera":"Static","negativePrompt":null,"visualStyle":null}"""));

        var output = await agent.GenerateAsync(new PromptAgentInput("n", "", "", SceneVisualType.AiVideo, null));

        Assert.Equal(ShotSize.Unspecified, output.Shot);
        Assert.Null(output.VisibleReferenceNames); // no names offered -> nothing to decide
        Assert.Null(output.CharacterOnScreen);
    }

    // ---- Storyboard service persistence ----

    private static (StoryboardService Service, Storyboard Storyboard, FakePromptAgent Agent) BuildService(ContentProject project)
    {
        var storyboard = Storyboard.Create(project.Id);
        var agent = new FakePromptAgent();
        var service = new StoryboardService(
            new FakeStoryboardRepository(storyboard), agent, new FakeContentProjectRepository(project),
            new StoryVisualContextResolver(new FakeStoryRepository()), NullLogger<StoryboardService>.Instance);
        return (service, storyboard, agent);
    }

    [Fact]
    public async Task Suggesting_a_prompt_stores_the_framing_the_visible_names_and_the_on_screen_decision()
    {
        var project = ContentProject.Create("Cats", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboard, agent) = BuildService(project);
        var scene = storyboard.AddScene(8, "Bastet was worshipped.", "", "", SceneVisualType.AiVideo);
        scene.SetRelevantReferenceLabels(new[] { "Bastet" }); // narration-name match from clip planning
        agent.Respond = _ => new PromptAgentOutput(
            "Rats scurry across a temple floor.", CameraMovement.Static, null, null,
            Shot: ShotSize.Wide, VisibleReferenceNames: Array.Empty<string>(), CharacterOnScreen: false);

        await service.SuggestScenePromptAsync(project.Id, scene.Id);

        Assert.Equal(ShotSize.Wide, scene.ShotSize);
        Assert.Empty(scene.RelevantReferenceLabels); // the shot shows nobody, whatever the narration names
        Assert.False(scene.CharacterOnScreen);
        Assert.False(SceneResponse.FromDomain(scene).CharacterRequired);
    }

    [Fact]
    public async Task Without_offered_names_or_a_decision_the_existing_tags_and_heuristic_are_left_alone()
    {
        var project = ContentProject.Create("Cats", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboard, _) = BuildService(project);
        var scene = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);
        scene.SetRelevantReferenceLabels(new[] { "Milo" });

        await service.SuggestScenePromptAsync(project.Id, scene.Id); // default fake output: no new fields

        Assert.Equal(new[] { "Milo" }, scene.RelevantReferenceLabels);
        Assert.Null(scene.CharacterOnScreen);
        Assert.True(SceneResponse.FromDomain(scene).CharacterRequired); // legacy: any AI-video scene
    }

    [Fact]
    public async Task The_agent_is_told_the_previous_shot_so_it_can_vary_it()
    {
        var project = ContentProject.Create("Cats", "topic", "storytelling", 60, "9:16", "en");
        var (service, storyboard, agent) = BuildService(project);
        var first = storyboard.AddScene(8, "a", "", "", SceneVisualType.AiVideo);
        first.SetGenerationPromptText("A cat walks.", null);
        first.SetShotSize(ShotSize.Medium);
        first.SetCameraMovement(CameraMovement.SlowPushIn);
        var second = storyboard.AddScene(8, "b", "", "", SceneVisualType.AiVideo);

        await service.SuggestScenePromptAsync(project.Id, first.Id);
        Assert.Null(agent.LastInput!.PreviousShot);

        await service.SuggestScenePromptAsync(project.Id, second.Id);
        Assert.Equal("Medium, Static", agent.LastInput!.PreviousShot); // first was just re-prompted to the fake's Static
    }

    // ---- Flow plan: character only when on screen ----

    private static async Task<FlowPlanScene> PlanSceneAsync(Guid projectId, Scene scene, Storyboard storyboard, params AssetReference[] references)
    {
        var project = ContentProject.Create("Cats", "topic", "storytelling", 60, "9:16", "en");
        var refs = new FakeAssetReferenceRepository();
        refs.Rows.AddRange(references);
        var ledger = new CreditLedgerService(new InMemoryGenerationAttemptRepository(), Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        var service = new FlowGenerationPlanService(
            new FakeContentProjectRepository(project), new FakeStoryboardRepository(storyboard), refs, ledger,
            new FakeStoryVisualContextResolver(), Options.Create(new CreditCostOptions()), Options.Create(new FlowModelOptions()));

        var plan = await service.BuildAsync(projectId);
        return plan.Scenes.Single(s => s.SceneId == scene.Id);
    }

    private static AssetReference ApprovedCharacter(Guid projectId, string? label)
    {
        var reference = AssetReference.CreateGenerated(projectId, AssetReferenceType.Character, "path/c.png", null, "upload", label: label);
        reference.Approve();
        return reference;
    }

    [Fact]
    public async Task A_shot_judged_to_show_no_character_gets_no_character_image_name_or_consistency_line()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard.Create(projectId);
        var rats = storyboard.AddScene(8, "Rats threatened the harvest.", "", "", SceneVisualType.AiVideo);
        rats.SetGenerationPromptText("A swarm of rats scurries across a temple floor.", null);
        rats.SetRelevantReferenceLabels(new[] { "Bastet" }); // stale tag
        rats.SetCharacterOnScreen(false);

        var scene = await PlanSceneAsync(projectId, rats, storyboard, ApprovedCharacter(projectId, "Bastet"));

        Assert.False(scene.CharacterRequired);
        Assert.Empty(scene.ReferenceImageUrls);
        Assert.Empty(scene.CharacterReferenceImages);
        Assert.DoesNotContain("Bastet", scene.FlowVideoPrompt);
        Assert.DoesNotContain("consistent character appearance", scene.FlowVideoPrompt);
    }

    [Fact]
    public async Task A_shot_judged_to_show_the_character_attaches_and_names_it()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard.Create(projectId);
        var cat = storyboard.AddScene(8, "Enter the African wildcat.", "", "", SceneVisualType.AiVideo);
        cat.SetGenerationPromptText("A golden cat steps into a sunbeam.", null);
        cat.SetRelevantReferenceLabels(new[] { "Bastet" });
        cat.SetCharacterOnScreen(true);
        cat.SetShotSize(ShotSize.CloseUp);

        var scene = await PlanSceneAsync(projectId, cat, storyboard, ApprovedCharacter(projectId, "Bastet"));

        Assert.True(scene.CharacterRequired);
        Assert.Single(scene.ReferenceImageUrls);
        Assert.Contains("(refer to the attached Bastet reference)", scene.FlowVideoPrompt);
        Assert.StartsWith("Close-up, ", scene.FlowVideoPrompt);
    }

    [Fact]
    public async Task An_undecided_legacy_scene_keeps_the_old_behaviour()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard.Create(projectId);
        var legacy = storyboard.AddScene(8, "n", "", "", SceneVisualType.AiVideo);
        legacy.SetGenerationPromptText("A cat walks.", null);

        var scene = await PlanSceneAsync(projectId, legacy, storyboard, ApprovedCharacter(projectId, null));

        Assert.True(scene.CharacterRequired);
        Assert.Single(scene.ReferenceImageUrls);
        Assert.Contains("consistent character appearance", scene.FlowVideoPrompt);
    }
}
