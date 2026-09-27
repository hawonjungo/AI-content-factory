using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The 5 required real-world scenarios for the script-driven reference-image
/// fix. Runs the actual <see cref="AssetReferencePromptAgent"/> production
/// code path end-to-end (input construction -> system/user prompt -> JSON
/// parse -> negative-prompt merge) against a stubbed LLM boundary - this
/// keeps the suite fast and free of real AI spend (per project policy, a real
/// Gemini call is never made in tests without being explicitly asked for)
/// while still exercising every line of the fix for real. The stub responses
/// are hand-written to represent what a compliant model response looks like;
/// what these tests actually prove is that (a) the request our code sends
/// correctly carries the scenario's script/style/mood into the prompt, and
/// (b) a correct model response flows through to the final prompt/negative
/// unmodified - i.e. nothing in our code re-introduces the old hardcoded
/// "always human" / "always bright" behaviour.
/// </summary>
public class AssetReferenceScenarioTests
{
    private sealed class StubLlmProvider : ILlmProvider
    {
        private readonly string _response;
        public string? LastSystemPrompt { get; private set; }
        public string? LastUserPrompt { get; private set; }
        public bool IsConfigured => true;

        public StubLlmProvider(string response) => _response = response;

        public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            return Task.FromResult(_response);
        }
    }

    private static async Task<(AssetReferencePromptOutput Output, StubLlmProvider Stub)> Run(
        string storyContext,
        string styleGuidance,
        string? styleNegative,
        string modelJson,
        string assetType = "Character",
        string? storyType = null,
        string? emotion = null)
    {
        var stub = new StubLlmProvider(modelJson);
        var agent = new AssetReferencePromptAgent(stub);

        var input = new AssetReferencePromptAgentInput(
            AssetType: assetType,
            Title: "Test Video",
            Topic: null,
            Niche: "Kitten / Cat",
            StoryContext: storyContext,
            StoryType: storyType,
            HookStyle: null,
            Emotion: emotion,
            StyleGuidance: styleGuidance,
            StyleNegativePrompt: styleNegative,
            AspectRatio: "9:16");

        var output = await agent.GenerateAsync(input);
        return (output, stub);
    }

    // ---- Test 1: Kitten (no random human as main subject) ----

    [Fact]
    public async Task Scenario1_kitten_lost_in_city_keeps_kitten_as_subject_with_city_environment()
    {
        const string story = "A small orange kitten gets lost in a busy city and searches for its owner.";
        const string modelJson = """
            {"analysis":{"mainSubjects":["small orange kitten"],"action":"searching, looking around anxiously","environment":"busy city street","emotion":"worried, searching","tone":"emotional storytelling","visualStyle":"photorealistic","lighting":"natural daylight","colorPalette":"urban neutral tones","composition":"vertical 9:16, medium shot"},"prompt":"A small orange kitten alone on a busy city street, looking around anxiously for its owner. Keep the kitten as the only main character - no humans, no other animals.","negativePrompt":"human, person, other animals"}
            """;

        var (output, stub) = await Run(story, "photorealistic documentary footage, natural daylight", "cartoon, illustration", modelJson);

        Assert.Contains("kitten", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Single(output.Analysis.MainSubjects);
        Assert.Contains("kitten", output.Analysis.MainSubjects[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("city", output.Prompt, StringComparison.OrdinalIgnoreCase);
        // The request our code sent must have carried the actual story, not a generic niche prompt.
        Assert.Contains("busy city", stub.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("human, person", output.NegativePrompt, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Test 2: Cartoon / 3D animated style ----

    [Fact]
    public async Task Scenario2_cartoon_kitten_is_not_photorealistic()
    {
        const string story = "A funny kitten accidentally steals a giant fish in a colorful 3D animated world.";
        const string modelJson = """
            {"analysis":{"mainSubjects":["playful kitten"],"action":"stealing a giant fish, running off comically","environment":"colorful stylized village","emotion":"funny, playful","tone":"comedic, lighthearted","visualStyle":"3d animated","lighting":"bright stylized lighting","colorPalette":"vivid saturated colors","composition":"vertical 9:16, dynamic angle"},"prompt":"A playful 3D animated kitten comically running off with a giant fish nearly as big as itself, in a colorful stylized village. Stylized 3D animated character design, not photorealistic. Keep the kitten as the only main character.","negativePrompt":"photorealistic, photograph, realistic photographic texture, human"}
            """;

        var (output, stub) = await Run(story, "stylized 3d animated film still, soft global illumination, rounded appealing character design", "photorealistic, horror, harsh shadows", modelJson);

        Assert.Contains("kitten", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3d animated", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photorealistic photograph", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photorealistic", output.NegativePrompt, StringComparison.OrdinalIgnoreCase);
        // The mandatory style guidance must reach the model.
        Assert.Contains("stylized 3d animated film still", stub.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Test 3: Dark / mysterious tone ----

    [Fact]
    public async Task Scenario3_dark_black_cat_gets_lowkey_lighting_not_bright_colors()
    {
        const string story = "A lonely black cat walks through an abandoned house at midnight, hearing mysterious sounds behind the door.";
        const string modelJson = """
            {"analysis":{"mainSubjects":["black cat"],"action":"walking cautiously, ears alert","environment":"abandoned house interior","emotion":"tense, wary","tone":"dark mysterious","visualStyle":"cinematic photorealistic","lighting":"low-key, deep shadows","colorPalette":"muted dark grey and black","composition":"vertical 9:16, tight framing"},"prompt":"A black cat walking cautiously through a dark, abandoned house at midnight, ears alert, deep shadows around it, one door slightly ajar in the background. Low-key cinematic lighting, muted dark color palette, dramatic shadows. Keep the cat as the only main character - no humans.","negativePrompt":"bright cheerful lighting, oversaturated colors, happy sunny atmosphere, human"}
            """;

        var (output, stub) = await Run(story, "cinematic film still, dramatic low-key lighting, deep shadows, cool teal and amber grade", "flat lighting, oversaturated colors", modelJson, storyType: "Kinh dị / bóc phốt", emotion: "Căng thẳng");

        Assert.Contains("black cat", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low-key", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shadow", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bright cheerful", output.NegativePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bright cheerful", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("midnight", stub.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Test 4: Emotional tone ----

    [Fact]
    public async Task Scenario4_emotional_old_cat_gets_appropriate_mood_and_relevant_window_environment()
    {
        const string story = "An old cat waits every evening at the same window for its owner to return.";
        const string modelJson = """
            {"analysis":{"mainSubjects":["old grey cat"],"action":"sitting still, watching, waiting","environment":"windowsill at dusk","emotion":"longing, patient sadness","tone":"emotional, bittersweet","visualStyle":"cinematic photorealistic","lighting":"soft warm dusk light fading to blue","colorPalette":"warm amber fading to cool blue","composition":"vertical 9:16, close medium shot"},"prompt":"An old grey cat sitting still on a windowsill at dusk, watching the street below with patient, longing eyes, waiting for its owner. Soft warm dusk light fading to cool blue, cinematic emotional color grading. Keep the cat as the only main character - no humans.","negativePrompt":"bright flat lighting, cartoon, human"}
            """;

        var stub = new StubLlmProvider(modelJson);
        var agent = new AssetReferencePromptAgent(stub);
        var input = new AssetReferencePromptAgentInput(
            "Character", "Test Video", null, "Kitten / Cat", story,
            StoryType: "Kể chuyện cảm xúc", HookStyle: null, Emotion: "Buồn, luyến tiếc",
            StyleGuidance: "cinematic film still, dramatic lighting", StyleNegativePrompt: null, AspectRatio: "9:16");

        var output = await agent.GenerateAsync(input);

        Assert.Contains("old", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cat", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("window", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("waiting", output.Analysis.Action, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Buồn", stub.LastUserPrompt, StringComparison.Ordinal);
    }

    // ---- Test 5: Human + cat together (no substitution of either subject) ----

    [Fact]
    public async Task Scenario5_girl_and_kitten_keeps_both_subjects_no_substitution()
    {
        const string story = "A little girl finds an injured kitten in the rain and takes it home.";
        const string modelJson = """
            {"analysis":{"mainSubjects":["young girl","injured kitten"],"action":"gently cradling the kitten, walking home","environment":"rainy street","emotion":"tender, caring","tone":"emotional, heartwarming","visualStyle":"cinematic photorealistic","lighting":"soft overcast rain light","colorPalette":"cool wet blues with warm skin tones","composition":"vertical 9:16, medium shot"},"prompt":"A little girl gently cradling an injured kitten in her arms while walking home through the rain. Soft overcast lighting, cool rain-soaked colors with warm tender highlights. Keep both the girl and the kitten as the only main characters - no other people, no other animals.","negativePrompt":"other people, other animals, cartoon"}
            """;

        var (output, stub) = await Run(story, "cinematic film still, natural lighting", null, modelJson, emotion: "Ấm áp, xúc động");

        Assert.Contains("girl", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kitten", output.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, output.Analysis.MainSubjects.Count);
        Assert.Contains(output.Analysis.MainSubjects, s => s.Contains("girl", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(output.Analysis.MainSubjects, s => s.Contains("kitten", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("rain", stub.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
    }
}
