using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class CharacterReferencePromptTests
{
    private readonly Guid _project = Guid.NewGuid();

    // A deliberately cat-heavy script: the old builder would force an
    // "orange tabby cat" portrait for this.
    private static ScriptResponse CatScript() => new(
        Guid.NewGuid(), Guid.NewGuid(),
        Hook: "A stray cat walked into the office and nobody noticed.",
        Introduction: "This is the story of how a kitten became the team mascot.",
        Body: "The cat slept on a laptop. The mèo joined the standup. Everyone loved the feline.",
        Escalation: "Then the cat walked across the investor's pitch deck.",
        Payoff: "The round closed. The cat got a title.",
        CallToAction: "Follow for more.",
        DateTimeOffset.UtcNow);

    private (AssetReferenceGenerationService Service, FakeImageGenerationProvider Provider, FakeAssetReferenceRepository Repo, FakeAiUsageTracker Usage, ContentProject Project)
        Build(ScriptResponse? script = null)
    {
        var project = ContentProject.Create("Office Cat", "why cats love laptops", "storytelling", 60, "9:16", "en");
        var provider = new FakeImageGenerationProvider();
        var repo = new FakeAssetReferenceRepository();
        var usage = new FakeAiUsageTracker();
        var service = new AssetReferenceGenerationService(
            new FakeContentProjectRepository(project),
            repo,
            provider,
            new FakeScriptService(script ?? CatScript()),
            new FakeFileStorage(),
            usage,
            Options.Create(new PricingOptions()),
            NullLogger<AssetReferenceGenerationService>.Instance);
        return (service, provider, repo, usage, project);
    }

    // ---- Prompt builder ----

    [Fact]
    public void Default_prompt_enforces_a_photorealistic_human_portrait()
    {
        var prompt = CharacterReferencePromptBuilder.BuildDefault("T", "topic", "niche", "some story", "cinematic");

        Assert.Contains("photorealistic portrait of a human", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NO animals", prompt);
        Assert.Contains("NO anthro", prompt);
        Assert.Contains("NO furries", prompt);
        Assert.Contains("NO distortion", prompt);
        Assert.Contains("[Gender/Role]", prompt);
        Assert.Contains("[Age]", prompt);
        Assert.Contains("[Outfit/Style]", prompt);
        Assert.DoesNotContain("orange tabby cat", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tabby", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Negative_prompt_blocks_anthro_and_distortion_and_merges_the_style_negative()
    {
        var negative = CharacterReferencePromptBuilder.MergeNegative("flat lighting, watermark");

        Assert.Contains("anthro", negative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("furry", negative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deformed", negative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad anatomy", negative, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flat lighting", negative, StringComparison.OrdinalIgnoreCase); // style negative merged
    }

    [Fact]
    public void MergeNegative_deduplicates_case_insensitively()
    {
        var negative = CharacterReferencePromptBuilder.MergeNegative("Anthro, Watermark, watermark");

        var count = negative.Split(',', StringSplitOptions.TrimEntries)
            .Count(t => t.Equals("watermark", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, count);
    }

    // ---- Suggested prompt endpoint path ----

    [Fact]
    public async Task Suggested_character_prompt_stays_human_even_for_a_cat_heavy_story()
    {
        var (service, _, _, _, _) = Build(CatScript());

        var suggested = await service.BuildSuggestedPromptAsync(_project, AssetReferenceType.Character);

        Assert.Contains("photorealistic portrait of a human", suggested.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tabby cat", suggested.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anthro", suggested.NegativePrompt, StringComparison.OrdinalIgnoreCase);
        // The story text is still handed to the model as context.
        Assert.Contains("Story Context", suggested.Prompt);
    }

    // ---- Generation ----

    [Fact]
    public async Task Character_generation_requests_exactly_one_image_even_when_more_are_asked_for()
    {
        var (service, provider, repo, _, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 3, customPrompt: null);

        Assert.Equal(1, provider.Calls);
        Assert.Single(repo.Rows, r => r.Status == AssetReferenceStatus.Generated);
    }

    [Fact]
    public async Task An_edited_prompt_is_sent_to_the_provider_not_the_default()
    {
        var (service, provider, _, _, _) = Build();
        const string edited = "MY OWN PROMPT: a specific human founder in a hoodie";

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: edited);

        Assert.StartsWith(edited, provider.LastRequest!.Prompt);
        Assert.DoesNotContain("[Gender/Role]", provider.LastRequest!.Prompt); // default template not used
        // The anti-anthro safety negative is still applied to an edited prompt.
        Assert.Contains("anthro", provider.LastRequest!.NegativePrompt!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_generated_variant_persists_its_prompt_and_a_stored_image()
    {
        var (service, provider, repo, usage, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: null);

        var row = Assert.Single(repo.Rows);
        Assert.Equal(AssetReferenceStatus.Generated, row.Status);
        Assert.Equal(AssetReferenceType.Character, row.Type);
        Assert.Equal(provider.LastRequest!.Prompt, row.Prompt);
        Assert.False(string.IsNullOrWhiteSpace(row.ImagePath));
        Assert.Single(usage.Records); // cost tracked once, for one image
    }

    [Fact]
    public async Task A_provider_failure_is_rethrown_and_progress_is_cleared()
    {
        var (service, provider, _, _, project) = Build();
        provider.Throw = new HttpRequestException("image API 500");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GenerateAsync(_project, AssetReferenceType.Character, count: 1, customPrompt: null));

        Assert.True(project.Progress.IsIdle); // no stuck "working" state
    }

    [Fact]
    public async Task Environment_generation_is_unchanged_and_uses_the_environment_prompt()
    {
        var (service, provider, repo, _, _) = Build();

        await service.GenerateAsync(_project, AssetReferenceType.Environment, count: 2, customPrompt: null);

        Assert.Equal(2, provider.Calls); // environment still honours a small variant count
        Assert.Equal(2, repo.Rows.Count(r => r.Type == AssetReferenceType.Environment));
        Assert.Contains("establishing wide shot", provider.LastRequest!.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("photorealistic portrait of a human", provider.LastRequest!.Prompt, StringComparison.OrdinalIgnoreCase);
    }
}
