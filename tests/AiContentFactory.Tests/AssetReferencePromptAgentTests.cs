using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.AssetReferences;
using Xunit;

namespace AiContentFactory.Tests;

public class AssetReferencePromptAgentTests
{
    /// <summary>Returns queued responses in order, repeating the last one once the queue drains.</summary>
    private sealed class StubLlmProvider : ILlmProvider
    {
        private readonly Queue<string> _responses;
        public int Calls { get; private set; }
        public string? LastUserPrompt { get; private set; }
        public string? LastSystemPrompt { get; private set; }
        public bool IsConfigured => true;

        public StubLlmProvider(params string[] responses) => _responses = new Queue<string>(responses);

        public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(next);
        }
    }

    private static string ValidResponse(
        string subject = "small orange tabby kitten with green eyes",
        string prompt = "A small orange tabby kitten sitting alone. Keep the kitten as the only character.",
        string negative = "human, person, text, watermark") => $$"""
        {"analysis":{"mainSubjects":["{{subject}}"],"action":"sitting alone","environment":"empty street","emotion":"lonely","tone":"dark emotional","visualStyle":"3d animated","lighting":"low-key","colorPalette":"muted blue-gray","composition":"vertical 9:16"},"prompt":"{{prompt}}","negativePrompt":"{{negative}}"}
        """;

    private static AssetReferencePromptAgentInput Input(
        string assetType = "Character",
        string storyContext = "A tiny orange kitten sits alone under a broken street lamp during heavy rain.",
        string styleGuidance = "stylized 3d animated film still, soft global illumination",
        string? styleNegative = "photorealistic, horror") =>
        new(assetType, "Lost Kitten", "a kitten looking for home", "Kitten / Cat", storyContext,
            StoryType: "Kể chuyện", HookStyle: "Bí ẩn mở màn", Emotion: "Buồn",
            StyleGuidance: styleGuidance, StyleNegativePrompt: styleNegative, AspectRatio: "9:16");

    [Fact]
    public async Task Parses_a_valid_response_into_the_analysis_and_prompt()
    {
        var agent = new AssetReferencePromptAgent(new StubLlmProvider(ValidResponse()));

        var result = await agent.GenerateAsync(Input());

        Assert.Single(result.Analysis.MainSubjects);
        Assert.Contains("kitten", result.Analysis.MainSubjects[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kitten", result.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("dark emotional", result.Analysis.Tone);
    }

    [Fact]
    public async Task Negative_prompt_merges_the_models_own_exclusions_with_the_style_negative_and_a_quality_safety_net()
    {
        var agent = new AssetReferencePromptAgent(new StubLlmProvider(ValidResponse(negative: "human, person")));

        var result = await agent.GenerateAsync(Input(styleNegative: "photorealistic, horror"));

        Assert.Contains("human", result.NegativePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("photorealistic", result.NegativePrompt, StringComparison.OrdinalIgnoreCase); // style negative merged
        Assert.Contains("blurry", result.NegativePrompt, StringComparison.OrdinalIgnoreCase); // quality safety net
    }

    [Fact]
    public void MergeWithQualityNegative_never_adds_a_species_or_style_term()
    {
        var negative = AssetReferencePromptAgent.MergeWithQualityNegative("flat lighting, watermark");

        Assert.Contains("flat lighting", negative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blurry", negative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animal", negative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anthro", negative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photorealistic", negative, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Type-specific negative prompt (deterministic in code, not left to the LLM) ----

    private static string[] Terms(string negative) => negative.Split(',', StringSplitOptions.TrimEntries);

    [Fact]
    public async Task Character_negative_prompt_always_excludes_backgrounds_even_when_the_model_forgets()
    {
        var agent = new AssetReferencePromptAgent(new StubLlmProvider(ValidResponse(negative: "photorealistic")));

        var result = await agent.GenerateAsync(Input(assetType: "Character", styleNegative: "flat lighting"));

        var terms = Terms(result.NegativePrompt);
        foreach (var expected in new[] { "background scenery", "environment", "landscape", "room interior", "furniture" })
        {
            Assert.Contains(expected, terms);
        }
        // A project-level Character reference may have several subjects ("boy and kitten") and a held item,
        // so the agent path must never carry the single-subject-only exclusions.
        Assert.DoesNotContain("other characters", terms);
        Assert.DoesNotContain("props", terms);
        Assert.Contains("photorealistic", terms); // model's own
        Assert.Contains("flat lighting", terms); // style's own
        Assert.Contains("blurry", terms); // quality net
        Assert.DoesNotContain("people", terms); // never blocks the subject itself
        Assert.DoesNotContain("silhouettes", terms);
    }

    [Fact]
    public async Task Environment_negative_prompt_always_excludes_people_even_when_the_model_forgets()
    {
        var agent = new AssetReferencePromptAgent(new StubLlmProvider(ValidResponse(negative: "photorealistic")));

        var result = await agent.GenerateAsync(Input(assetType: "Environment", styleNegative: "flat lighting"));

        var terms = Terms(result.NegativePrompt);
        foreach (var expected in new[] { "people", "person", "human", "characters", "figures", "silhouettes", "faces", "crowd", "animals as characters" })
        {
            Assert.Contains(expected, terms);
        }
        Assert.Contains("photorealistic", terms);
        Assert.Contains("flat lighting", terms);
        Assert.Contains("blurry", terms);
        Assert.DoesNotContain("background scenery", terms);
        Assert.DoesNotContain("environment", terms);
    }

    [Fact]
    public void MergeWithQualityNegative_by_type_adds_type_terms_once_and_keeps_style_and_quality()
    {
        var character = AssetReferencePromptAgent.MergeWithQualityNegative(AssetReferenceType.Character, "flat lighting, furniture");
        var environment = AssetReferencePromptAgent.MergeWithQualityNegative(AssetReferenceType.Environment, "flat lighting");

        var characterTerms = Terms(character);
        Assert.Contains("flat lighting", characterTerms);
        Assert.Contains("background scenery", characterTerms);
        Assert.Contains("blurry", characterTerms);
        Assert.Single(characterTerms, t => t.Equals("furniture", StringComparison.OrdinalIgnoreCase)); // de-duplicated against the style's own
        Assert.DoesNotContain("people", characterTerms);
        Assert.DoesNotContain("other characters", characterTerms);
        Assert.DoesNotContain("props", characterTerms);

        // Only an explicitly single-subject sheet (the Story-level character) adds them.
        var single = Terms(AssetReferencePromptAgent.MergeWithQualityNegative(AssetReferenceType.Character, null, singleSubject: true));
        Assert.Contains("other characters", single);
        Assert.Contains("props", single);
        // singleSubject never changes an Environment plate.
        Assert.DoesNotContain("props", Terms(AssetReferencePromptAgent.MergeWithQualityNegative(AssetReferenceType.Environment, null, singleSubject: true)));

        var environmentTerms = Terms(environment);
        Assert.Contains("people", environmentTerms);
        Assert.Contains("crowd", environmentTerms);
        Assert.DoesNotContain("background scenery", environmentTerms);
    }

    [Fact]
    public async Task Character_system_prompt_asks_for_a_neutral_identity_study_and_not_the_story_moment()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new AssetReferencePromptAgent(stub);

        await agent.GenerateAsync(Input(assetType: "Character"));

        var system = System.Text.RegularExpressions.Regex.Replace(stub.LastSystemPrompt!, @"\s+", " "); // ignore prompt line wrapping
        Assert.Contains("NEUTRAL IDENTITY STUDY", system);
        Assert.Contains("seamless solid WHITE studio background", system);
        Assert.DoesNotContain("light-grey", system);
        Assert.Contains("FULL BODY", system);
        Assert.Contains("neutral relaxed standing pose", system);
        Assert.Contains("A-pose", system);
        Assert.Contains("neutral calm expression", system);
        Assert.Contains("Nothing may hide an identity feature", system);
        Assert.Contains("never invent clothing", system);
        Assert.Contains("do NOT use the story's key-moment pose", system);
        Assert.Contains("do NOT let the story's mood pick a dark", system);
        // The old "stage the character in the story" wording is gone.
        Assert.DoesNotContain("staged in a setting/lighting consistent with", system);
        Assert.DoesNotContain("pose and expression reflecting the story's key moment", system);
        // Locks that must survive.
        Assert.Contains("SUBJECT LOCK", system);
        Assert.Contains("STYLE LOCK", system);
        // The example ending must not tell a multi-subject story ("boy and kitten") to keep only one character.
        Assert.DoesNotContain("as the only character", system);
        Assert.Contains("adds nobody else", system);
    }

    [Fact]
    public async Task Character_negative_prompt_also_excludes_the_pose_and_composition_failures_of_a_reference_sheet()
    {
        var agent = new AssetReferencePromptAgent(new StubLlmProvider(ValidResponse(negative: "photorealistic")));

        var result = await agent.GenerateAsync(Input(assetType: "Character", styleNegative: null));

        var terms = Terms(result.NegativePrompt);
        foreach (var expected in new[] { "cropped", "cut off", "sitting", "crouching", "crossed arms", "leaning", "dynamic pose", "extreme perspective", "side view", "gradient background", "patterned background", "food" })
        {
            Assert.Contains(expected, terms);
        }

        Assert.Equal(terms.Length, terms.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Environment plates are unaffected.
        var environment = Terms(AssetReferencePromptAgent.MergeWithQualityNegative(AssetReferenceType.Environment, null));
        Assert.DoesNotContain("sitting", environment);
        Assert.DoesNotContain("cropped", environment);
    }

    [Fact]
    public async Task Environment_system_prompt_asks_for_an_empty_plate_with_no_characters()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new AssetReferencePromptAgent(stub);

        await agent.GenerateAsync(Input(assetType: "Environment"));

        var system = System.Text.RegularExpressions.Regex.Replace(stub.LastSystemPrompt!, @"\s+", " "); // ignore prompt line wrapping
        Assert.Contains("EMPTY PLATE", system);
        Assert.Contains("wide/establishing view", system);
        Assert.Contains("no foreground subject", system);
        Assert.Contains("Empty scene, no people, no characters.", system);
    }

    [Fact]
    public async Task System_prompt_json_output_schema_is_unchanged()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new AssetReferencePromptAgent(stub);

        await agent.GenerateAsync(Input());

        Assert.Contains(
            """{"analysis":{"mainSubjects":[string],"action":string,"environment":string,"emotion":string,"tone":string,"visualStyle":string,"lighting":string,"colorPalette":string,"composition":string},"prompt":string,"negativePrompt":string}""",
            stub.LastSystemPrompt);
    }

    [Fact]
    public async Task System_prompt_locks_subject_style_and_mood_from_the_script_not_the_niche()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new AssetReferencePromptAgent(stub);

        await agent.GenerateAsync(Input());

        Assert.Contains("SUBJECT LOCK", stub.LastSystemPrompt);
        Assert.Contains("STYLE LOCK", stub.LastSystemPrompt);
        Assert.Contains("MOOD LOCK", stub.LastSystemPrompt);
        Assert.Contains("niche sounds cute", stub.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task User_prompt_carries_the_story_context_style_guidance_and_idea_hints()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new AssetReferencePromptAgent(stub);

        await agent.GenerateAsync(Input());

        var prompt = stub.LastUserPrompt!;
        Assert.Contains("Asset type: Character", prompt);
        Assert.Contains("broken street lamp", prompt); // story context passed through
        Assert.Contains("stylized 3d animated film still", prompt); // style guidance passed through
        Assert.Contains("Kể chuyện", prompt); // story type hint
        Assert.Contains("Buồn", prompt); // emotion hint
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var agent = new AssetReferencePromptAgent(new StubLlmProvider("not json", "still not json"));

        await Assert.ThrowsAsync<AgentGenerationException>(() => agent.GenerateAsync(Input()));
    }

    [Fact]
    public async Task Missing_main_subject_is_rejected_then_repaired_on_retry()
    {
        var invalid = """{"analysis":{"mainSubjects":[],"action":"a","environment":"b","emotion":"c","tone":"d","visualStyle":"e","lighting":"f","colorPalette":"g","composition":"h"},"prompt":"p","negativePrompt":"n"}""";
        var stub = new StubLlmProvider(invalid, ValidResponse());
        var agent = new AssetReferencePromptAgent(stub);

        var result = await agent.GenerateAsync(Input());

        Assert.NotEmpty(result.Analysis.MainSubjects);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task Json_wrapped_in_a_markdown_fence_is_still_parsed()
    {
        var fenced = "```json\n" + ValidResponse() + "\n```";
        var agent = new AssetReferencePromptAgent(new StubLlmProvider(fenced));

        var result = await agent.GenerateAsync(Input());

        Assert.Contains("kitten", result.Prompt, StringComparison.OrdinalIgnoreCase);
    }
}
