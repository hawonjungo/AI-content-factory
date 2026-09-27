using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="SceneAssetGenerator"/>'s per-scene reference selection:
/// a Story-linked scene tagged via <see cref="Scene.RelevantReferenceLabels"/>
/// gets only the references for the names it mentions (capped at Veo's
/// 3-reference limit, Environment/location first); every other scene falls
/// back to the classic Character+Environment pair, unchanged from before
/// this feature existed.
/// </summary>
public class SceneAssetGeneratorReferenceSelectionTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static ReferenceImage Image(string label) => new(new byte[] { 1, 2, 3 }, "image/png", label, null);

    private (SceneAssetGenerator Generator, FakeVideoGenerationProvider Video) BuildGenerator()
    {
        var video = new FakeVideoGenerationProvider();
        var image = new FakeImageGenerationProvider();
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);

        var generator = new SceneAssetGenerator(
            storyboardService: null!,
            assetService: new FakeAssetService(),
            promptAgent: null!,
            videoProvider: video,
            imageProvider: image,
            ttsService: null!,
            audioTiming: null!,
            assetReferenceService: null!,
            quotaManager: new FakeGoogleFlowQuotaManager(),
            creditLedger: ledger,
            fileStorage: new FakeFileStorage(),
            usageTracker: new FakeAiUsageTracker(),
            storyVisualContextResolver: null!,
            pricing: Options.Create(new PricingOptions()),
            creditCosts: Options.Create(new CreditCostOptions()),
            flow: Options.Create(new GoogleFlowOptions()),
            videoOptions: Options.Create(new VideoGenerationOptions()), // ImageToVideoEnabled = false -> native referenceImages path
            logger: NullLogger<SceneAssetGenerator>.Instance);

        return (generator, video);
    }

    private SceneGenerationContext Context(
        ReferenceImage? legacyCharacter,
        ReferenceImage? legacyEnvironment,
        IReadOnlyList<ApprovedSceneReference> approvedReferences,
        IReadOnlyDictionary<string, CharacterBehaviorProfile>? characterBehaviorProfiles = null) =>
        new(
            _project,
            "9:16",
            PresetCatalog.ResolveStyle(null),
            null!,
            legacyCharacter,
            legacyEnvironment,
            approvedReferences,
            characterBehaviorProfiles);

    private static SceneResponse TaggedScene(string narration, IReadOnlyList<string> tags, string action = "a scene action")
    {
        var sb = Storyboard.Create(Guid.NewGuid());
        var scene = sb.AddScene(8, narration, string.Empty, "push", SceneVisualType.AiVideo);
        scene.SetGenerationPrompt(action, null, null, "test"); // pre-set so no prompt-agent call is needed
        if (tags.Count > 0)
        {
            scene.SetRelevantReferenceLabels(tags);
        }

        return SceneResponse.FromDomain(scene);
    }

    [Fact]
    public async Task Scene_tagged_with_one_name_selects_only_that_approved_reference()
    {
        var (generator, video) = BuildGenerator();

        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")),
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Mimi")),
            new ApprovedSceneReference(AssetReferenceType.Environment, Image("Ha Long Bay")),
        };
        var context = Context(legacyCharacter: null, legacyEnvironment: null, approved);
        var scene = TaggedScene("Milo climbed the mast.", new[] { "Milo" });

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var sent = Assert.Single(video.LastRequest!.ReferenceImages!);
        Assert.Equal("Milo", sent.Label);
    }

    [Fact]
    public async Task Untagged_scene_falls_back_to_the_classic_character_and_environment_pair()
    {
        var (generator, video) = BuildGenerator();

        var legacyCharacter = Image("Character");
        var legacyEnvironment = Image("Environment");
        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, legacyCharacter),
            new ApprovedSceneReference(AssetReferenceType.Environment, legacyEnvironment),
        };
        var context = Context(legacyCharacter, legacyEnvironment, approved);
        var scene = TaggedScene("A generic beat with no named cast.", Array.Empty<string>());

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var sent = video.LastRequest!.ReferenceImages!;
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, r => r.Label == "Character");
        Assert.Contains(sent, r => r.Label == "Environment");
    }

    [Fact]
    public async Task Tagged_names_with_no_approved_reference_fall_back_to_the_classic_pair()
    {
        var (generator, video) = BuildGenerator();

        var legacyCharacter = Image("Character");
        var legacyEnvironment = Image("Environment");
        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, legacyCharacter),
            new ApprovedSceneReference(AssetReferenceType.Environment, legacyEnvironment),
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")),
        };
        var context = Context(legacyCharacter, legacyEnvironment, approved);
        // Tagged with a name that has no approved reference yet.
        var scene = TaggedScene("Zephyr appeared out of nowhere.", new[] { "Zephyr" });

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var sent = video.LastRequest!.ReferenceImages!;
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, r => r.Label == "Character");
        Assert.Contains(sent, r => r.Label == "Environment");
    }

    [Fact]
    public async Task More_than_three_matches_are_capped_at_three_with_environment_first_then_narration_order()
    {
        var (generator, video) = BuildGenerator();

        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")),
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Mimi")),
            new ApprovedSceneReference(AssetReferenceType.Environment, Image("Ha Long Bay")),
            new ApprovedSceneReference(AssetReferenceType.Environment, Image("Hoi An Ancient Town")),
        };
        var context = Context(legacyCharacter: null, legacyEnvironment: null, approved);
        // Mimi is mentioned before Milo in the narration.
        var scene = TaggedScene(
            "Mimi and Milo sailed from Ha Long Bay toward Hoi An Ancient Town.",
            new[] { "Milo", "Mimi", "Ha Long Bay", "Hoi An Ancient Town" });

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var sent = video.LastRequest!.ReferenceImages!;
        Assert.Equal(3, sent.Count);
        // Both Environment matches come first (priority rule), filling 2 of the
        // 3 slots; Character matches fill what's left, ordered by first
        // mention in the narration - Mimi before Milo here.
        Assert.Equal("Ha Long Bay", sent[0].Label);
        Assert.Equal("Hoi An Ancient Town", sent[1].Label);
        Assert.Equal("Mimi", sent[2].Label);
    }

    [Fact]
    public async Task Non_story_project_scene_selects_the_classic_pair_exactly_as_before()
    {
        var (generator, video) = BuildGenerator();

        var legacyCharacter = Image("Character");
        var legacyEnvironment = Image("Environment");
        // A non-Story project never has any named rows - ApprovedReferences is
        // just the classic pair.
        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, legacyCharacter),
            new ApprovedSceneReference(AssetReferenceType.Environment, legacyEnvironment),
        };
        var context = Context(legacyCharacter, legacyEnvironment, approved);
        var scene = TaggedScene("A normal, non-Story scene.", Array.Empty<string>());

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var sent = video.LastRequest!.ReferenceImages!;
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, r => r.Label == "Character");
        Assert.Contains(sent, r => r.Label == "Environment");
    }

    [Fact]
    public async Task Tagged_scene_names_the_selected_character_and_location_in_the_prompt_text()
    {
        var (generator, video) = BuildGenerator();

        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")),
            new ApprovedSceneReference(AssetReferenceType.Environment, Image("Hanoi Old Quarter")),
        };
        var context = Context(legacyCharacter: null, legacyEnvironment: null, approved);
        var scene = TaggedScene("Milo wandered through Hanoi Old Quarter.", new[] { "Milo", "Hanoi Old Quarter" });

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var prompt = video.LastRequest!.Prompt;
        Assert.Contains("Milo", prompt);
        Assert.Contains("Hanoi Old Quarter", prompt);
        Assert.Contains("Setting: Hanoi Old Quarter.", prompt);
        Assert.Contains("Consistent character reference for Milo:", prompt);
    }

    [Fact]
    public async Task Non_story_project_prompt_text_is_unchanged_generic_wording_with_no_names()
    {
        var (generator, video) = BuildGenerator();

        var legacyCharacter = Image("Character");
        var legacyEnvironment = Image("Environment");
        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, legacyCharacter),
            new ApprovedSceneReference(AssetReferenceType.Environment, legacyEnvironment),
        };
        var context = Context(legacyCharacter, legacyEnvironment, approved);
        var scene = TaggedScene("A normal, non-Story scene.", Array.Empty<string>());

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var prompt = video.LastRequest!.Prompt;
        Assert.Contains("Consistent character reference: same face, body, hair, clothing.", prompt);
        Assert.Contains("Consistent environment reference: same location, lighting, mood.", prompt);
        Assert.DoesNotContain("Setting:", prompt);
    }

    // --- CharacterBehaviorProfiles wiring (real Build() path via the actual
    //     provider request, not just VideoPromptBuilderTests' direct unit
    //     calls) -----------------------------------------------------------

    [Fact]
    public async Task A_cat_flagged_character_with_a_matching_action_gets_the_behavior_clause_in_the_real_provider_prompt()
    {
        var (generator, video) = BuildGenerator();

        var approved = new[] { new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")) };
        var context = Context(
            legacyCharacter: null, legacyEnvironment: null, approved,
            characterBehaviorProfiles: new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat });
        var scene = TaggedScene(
            "narration", new[] { "Milo" }, action: "Milo sits upright on the stool and watches the door");

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        Assert.Contains("anthropomorphically", video.LastRequest!.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mixed_scene_only_the_flagged_characters_clause_reaches_the_real_provider_prompt()
    {
        var (generator, video) = BuildGenerator();

        var approved = new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")),
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Rex")),
        };
        var context = Context(
            legacyCharacter: null, legacyEnvironment: null, approved,
            characterBehaviorProfiles: new Dictionary<string, CharacterBehaviorProfile>
            {
                ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
                ["Rex"] = CharacterBehaviorProfile.None,
            });
        var scene = TaggedScene(
            "narration", new[] { "Milo", "Rex" }, action: "Milo sits upright on the stool while Rex stands quietly nearby");

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        var prompt = video.LastRequest!.Prompt;
        var occurrences = System.Text.RegularExpressions.Regex.Matches(prompt, "anthropomorphically").Count;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public async Task No_CharacterBehaviorProfiles_on_the_context_is_unaffected_same_as_before_this_feature_existed()
    {
        var (generator, video) = BuildGenerator();

        var approved = new[] { new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")) };
        var context = Context(legacyCharacter: null, legacyEnvironment: null, approved); // characterBehaviorProfiles: null (default)
        var scene = TaggedScene("narration", new[] { "Milo" }, action: "Milo sits upright on the stool");

        await generator.GenerateClipAsync(context, scene, refreshPrompt: false);

        Assert.DoesNotContain("anthropomorphically", video.LastRequest!.Prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneResponse DecidedScene(bool? characterOnScreen, ShotSize shot = ShotSize.Unspecified, params string[] tags)
    {
        var sb = Storyboard.Create(Guid.NewGuid());
        var scene = sb.AddScene(8, "Rats threatened the harvest.", string.Empty, "push", SceneVisualType.AiVideo);
        scene.SetGenerationPrompt("A swarm of rats scurries across a temple floor", null, null, "test");
        scene.SetCharacterOnScreen(characterOnScreen);
        scene.SetShotSize(shot);
        if (tags.Length > 0)
        {
            scene.SetRelevantReferenceLabels(tags);
        }

        return SceneResponse.FromDomain(scene);
    }

    [Fact]
    public async Task A_shot_judged_to_show_no_character_never_sends_the_character_image_or_consistency_line()
    {
        var (generator, video) = BuildGenerator();
        var legacyCharacter = Image("Character");
        var legacyEnvironment = Image("Environment");
        var context = Context(legacyCharacter, legacyEnvironment, new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, legacyCharacter),
            new ApprovedSceneReference(AssetReferenceType.Environment, legacyEnvironment),
        });

        await generator.GenerateClipAsync(context, DecidedScene(characterOnScreen: false), refreshPrompt: false);

        var sent = Assert.Single(video.LastRequest!.ReferenceImages!);
        Assert.Equal("Environment", sent.Label);
        Assert.DoesNotContain("Consistent character reference", video.LastRequest.Prompt);
    }

    [Fact]
    public async Task A_stale_character_tag_is_ignored_when_the_shot_shows_no_character()
    {
        var (generator, video) = BuildGenerator();
        var context = Context(null, null, new[]
        {
            new ApprovedSceneReference(AssetReferenceType.Character, Image("Milo")),
            new ApprovedSceneReference(AssetReferenceType.Environment, Image("Ha Long Bay")),
        });

        await generator.GenerateClipAsync(context, DecidedScene(false, ShotSize.Unspecified, "Milo", "Ha Long Bay"), refreshPrompt: false);

        var sent = Assert.Single(video.LastRequest!.ReferenceImages!);
        Assert.Equal("Ha Long Bay", sent.Label);
        Assert.DoesNotContain("Milo", video.LastRequest.Prompt);
    }

    [Fact]
    public async Task The_direct_Veo_prompt_carries_the_scene_framing()
    {
        var (generator, video) = BuildGenerator();
        var context = Context(null, null, Array.Empty<ApprovedSceneReference>());

        await generator.GenerateClipAsync(context, DecidedScene(null, ShotSize.Wide), refreshPrompt: false);

        Assert.Contains("Wide shot, slow subtle push-in.", video.LastRequest!.Prompt);
    }
}
