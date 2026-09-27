using AiContentFactory.Application.Agents;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers the fix for the reference-image prompt losing script context: the
/// service used to build the Character prompt from a static template that
/// hardcoded "a human" (and an anti-animal negative) regardless of the actual
/// script. It now asks <see cref="IAssetReferencePromptAgent"/>, which is fed
/// the real script/style/idea-config context - these tests assert the service
/// wires that context through correctly and never reintroduces a hardcoded
/// subject or a species-blocking negative.
/// </summary>
public class AssetReferenceGenerationServiceTests
{
    private readonly Guid _project = Guid.NewGuid();

    // A deliberately cat-heavy script: the old template would force an "a
    // human" portrait for this regardless.
    private static ScriptResponse CatScript() => new(
        Guid.NewGuid(), Guid.NewGuid(),
        Hook: "A stray kitten walked into the office and nobody noticed.",
        Introduction: "This is the story of how a kitten became the team mascot.",
        Body: "The kitten slept on a laptop. Everyone loved the feline.",
        Escalation: "Then the kitten walked across the investor's pitch deck.",
        Payoff: "The round closed. The kitten got a title.",
        CallToAction: "Follow for more.",
        DateTimeOffset.UtcNow);

    private (AssetReferenceGenerationService Service, FakeImageGenerationProvider Provider, FakeAssetReferenceRepository Repo, FakeAiUsageTracker Usage, FakeAssetReferencePromptAgent Agent, ContentProject Project)
        Build(ScriptResponse? script = null, ContentIdeaConfig? ideaConfig = null)
    {
        var project = ContentProject.Create("Office Kitten", "why a kitten runs the standup", "storytelling", 60, "9:16", "en");
        if (ideaConfig is not null)
        {
            project.UpdateIdeaConfig(ideaConfig);
        }

        var provider = new FakeImageGenerationProvider();
        var repo = new FakeAssetReferenceRepository();
        var usage = new FakeAiUsageTracker();
        var agent = new FakeAssetReferencePromptAgent();
        var service = new AssetReferenceGenerationService(
            new FakeContentProjectRepository(project),
            repo,
            provider,
            agent,
            new FakeScriptService(script ?? CatScript()),
            new FakeFileStorage(),
            usage,
            Options.Create(new PricingOptions()),
            NullLogger<AssetReferenceGenerationService>.Instance);
        return (service, provider, repo, usage, agent, project);
    }

    // ---- Default (agent-built) prompt path ----

    [Fact]
    public async Task Suggested_prompt_sends_the_actual_script_style_and_idea_hints_to_the_agent()
    {
        var idea = ContentIdeaConfig.Create(
            contentPillar: null, targetAudience: null,
            storyType: "Kể chuyện", hookStyle: "Câu hỏi sốc", emotion: "Ấm áp",
            voiceGender: VoiceGender.Unspecified, voiceStyle: null, speakingRate: null,
            narrationLanguage: null, creditStrategy: CreditStrategy.Balanced);
        var (service, _, _, _, agent, _) = Build(ideaConfig: idea);

        await service.BuildSuggestedPromptAsync(_project, AssetReferenceType.Character);

        Assert.NotNull(agent.LastInput);
        Assert.Equal("Character", agent.LastInput!.AssetType);
        Assert.Equal("Office Kitten", agent.LastInput.Title);
        Assert.Contains("kitten", agent.LastInput.StoryContext, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Kể chuyện", agent.LastInput.StoryType);
        Assert.Equal("Câu hỏi sốc", agent.LastInput.HookStyle);
        Assert.Equal("Ấm áp", agent.LastInput.Emotion);
        Assert.False(string.IsNullOrWhiteSpace(agent.LastInput.StyleGuidance)); // the project's style preset, never blank
    }

    [Fact]
    public async Task Environment_prompt_request_carries_no_character_specific_data_but_still_gets_the_script()
    {
        var (service, _, _, _, agent, _) = Build();

        await service.BuildSuggestedPromptAsync(_project, AssetReferenceType.Environment);

        Assert.Equal("Environment", agent.LastInput!.AssetType);
        Assert.Contains("kitten", agent.LastInput.StoryContext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generated_prompt_preserves_whatever_subject_the_agent_derives_from_the_script()
    {
        var (service, provider, _, _, agent, _) = Build();
        agent.Respond = input => new AssetReferencePromptOutput(
            new AssetReferenceVisualAnalysis(new[] { "small orange tabby kitten" }, "sitting", "office desk", "curious", "warm", "3d animated", "soft", "warm palette", "medium shot"),
            "A small orange tabby kitten sitting on an office desk. Keep the kitten as the only character.",
            "human, person, text, watermark");

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: null);

        Assert.Contains("kitten", provider.LastRequest!.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("human", provider.LastRequest!.Prompt, StringComparison.OrdinalIgnoreCase);
        // The negative prompt the agent derived (excluding "human") is used as-is - the
        // service never forces an anti-animal exclusion back in on top of it.
        Assert.Contains("human", provider.LastRequest!.NegativePrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Character_generation_requests_exactly_one_image_even_when_more_are_asked_for()
    {
        var (service, provider, repo, _, _, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 3, customPrompt: null);

        Assert.Equal(1, provider.Calls);
        Assert.Single(repo.Rows, r => r.Status == AssetReferenceStatus.Generated);
    }

    [Fact]
    public async Task Environment_generation_honours_a_small_variant_count_reusing_one_resolved_prompt()
    {
        var (service, provider, repo, _, agent, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Environment, count: 2, customPrompt: null);

        Assert.Equal(2, provider.Calls);
        Assert.Equal(2, repo.Rows.Count(r => r.Type == AssetReferenceType.Environment));
        // The prompt is resolved once and reused for every variant image - same
        // behaviour as before this change, just now via the agent.
        Assert.Equal(1, agent.Calls);
    }

    [Fact]
    public async Task The_generated_variant_persists_the_agents_prompt_and_a_stored_image()
    {
        var (service, provider, repo, usage, _, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: null);

        var row = Assert.Single(repo.Rows);
        Assert.Equal(AssetReferenceStatus.Generated, row.Status);
        Assert.Equal(AssetReferenceType.Character, row.Type);
        Assert.Equal(provider.LastRequest!.Prompt, row.Prompt);
        Assert.False(string.IsNullOrWhiteSpace(row.ImagePath));
        Assert.Single(usage.Records);
    }

    [Fact]
    public async Task A_provider_failure_is_rethrown_and_progress_is_cleared()
    {
        var (service, provider, _, _, _, project) = Build();
        provider.Throw = new HttpRequestException("image API 500");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: null));

        Assert.True(project.Progress.IsIdle); // no stuck "working" state
    }

    // ---- Custom (user-edited) prompt path - never routed through the agent ----

    [Fact]
    public async Task An_edited_prompt_is_sent_verbatim_and_the_agent_is_never_called()
    {
        var (service, provider, _, _, agent, _) = Build();
        const string edited = "MY OWN PROMPT: a specific orange kitten on a windowsill";

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: edited);

        Assert.StartsWith(edited, provider.LastRequest!.Prompt);
        Assert.Null(agent.LastInput); // default prompt path never touched
    }

    [Fact]
    public async Task An_edited_prompts_negative_prompt_is_quality_safety_only_never_a_hardcoded_species_exclusion()
    {
        var (service, provider, _, _, _, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: "a kitten on a windowsill");

        var negative = provider.LastRequest!.NegativePrompt!;
        // This is the regression the whole fix is about: the old code always
        // added an anti-animal exclusion here, fighting a script the user
        // explicitly wrote to be about an animal.
        Assert.DoesNotContain("anthro", negative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("furry", negative, StringComparison.OrdinalIgnoreCase);
        var terms = negative.Split(',', StringSplitOptions.TrimEntries);
        Assert.DoesNotContain(terms, t => t.Equals("animal", StringComparison.OrdinalIgnoreCase));
        // The genuinely subject-agnostic quality safety net is still applied.
        Assert.Contains("blurry", negative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deformed", negative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_edited_Character_prompt_gets_the_background_exclusions_but_its_wording_is_sent_verbatim()
    {
        var (service, provider, _, _, agent, _) = Build();
        const string edited = "a kitten on a windowsill";

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: edited);

        Assert.StartsWith(edited + "\n\nVisual style:", provider.LastRequest!.Prompt.Replace("\r\n", "\n"));
        Assert.Null(agent.LastInput);
        var terms = provider.LastRequest.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries);
        foreach (var expected in new[] { "background scenery", "environment", "landscape", "room interior", "furniture" })
        {
            Assert.Contains(expected, terms);
        }
        Assert.DoesNotContain("other characters", terms); // not single-subject-safe on the project-level path
        Assert.DoesNotContain("props", terms);
        Assert.DoesNotContain("people", terms);
    }

    [Fact]
    public async Task An_edited_Environment_prompt_gets_the_people_exclusions_but_its_wording_is_sent_verbatim()
    {
        var (service, provider, _, _, agent, _) = Build();
        const string edited = "a quiet office at dawn";

        await service.GenerateAsync(_project, AssetReferenceType.Environment, count: 1, customPrompt: edited);

        Assert.StartsWith(edited + "\n\nVisual style:", provider.LastRequest!.Prompt.Replace("\r\n", "\n"));
        Assert.Null(agent.LastInput);
        var terms = provider.LastRequest.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries);
        foreach (var expected in new[] { "people", "person", "human", "characters", "figures", "silhouettes", "crowd" })
        {
            Assert.Contains(expected, terms);
        }
        Assert.DoesNotContain("background scenery", terms);
    }

    // ---- Reference prompts use only the style's LOOK-ONLY fields ----

    public static IEnumerable<object[]> AllStyleIds() => PresetCatalog.Styles.Select(x => new object[] { x.Id });

    [Theory]
    [MemberData(nameof(AllStyleIds))]
    public async Task The_agent_input_carries_the_look_only_style_fields_never_the_scene_oriented_ones(string styleId)
    {
        var (service, _, _, _, agent, project) = Build();
        project.ApplyPresets(null, styleId, null, null, null);
        var style = PresetCatalog.FindStyle(styleId)!;

        await service.BuildSuggestedPromptAsync(_project, AssetReferenceType.Character);
        Assert.Equal(style.ReferenceLookGuidance, agent.LastInput!.StyleGuidance);
        Assert.Equal(style.ReferenceNegativePrompt, agent.LastInput.StyleNegativePrompt);

        await service.BuildSuggestedPromptAsync(_project, AssetReferenceType.Environment);
        Assert.Equal(style.ReferenceLookGuidance, agent.LastInput!.StyleGuidance);
        Assert.NotEqual(style.VisualStyleGuidance, agent.LastInput.StyleGuidance);
    }

    [Theory]
    [MemberData(nameof(AllStyleIds))]
    public async Task An_edited_prompts_style_line_and_negative_use_the_look_only_style_fields(string styleId)
    {
        var (service, provider, _, _, _, project) = Build();
        project.ApplyPresets(null, styleId, null, null, null);
        var style = PresetCatalog.FindStyle(styleId)!;

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: "a kitten");

        var request = provider.LastRequest!;
        Assert.Contains($"Visual style: {style.ReferenceLookGuidance}.", request.Prompt);
        Assert.DoesNotContain(style.VisualStyleGuidance, request.Prompt);
        if (style.NegativePrompt is not null)
        {
            Assert.DoesNotContain(style.NegativePrompt, request.NegativePrompt!);
        }
        var terms = request.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries);
        foreach (var term in style.ReferenceNegativePrompt.Split(',', StringSplitOptions.TrimEntries))
        {
            Assert.Contains(term, terms);
        }
    }
}
