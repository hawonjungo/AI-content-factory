using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class FlowPlanServiceTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static Storyboard StoryboardWith(Guid project)
    {
        var sb = Storyboard.Create(project);
        var hook = sb.AddScene(8, "The stray cat walked into the office.", "wide establishing shot", "slow push in", SceneVisualType.AiVideo);
        hook.SetAllocation(100, "Fast", "Hero clip: highest AI-video priority.");
        var beat1 = sb.AddScene(8, "It joined the standup rotation.", "cat on a chair", "handheld", SceneVisualType.AiVideo);
        beat1.SetAllocation(70, "Lite", "Story-beat clip.");
        var beat2 = sb.AddScene(8, "Someone explained a bug to it.", "engineer talking", "static", SceneVisualType.AiVideo);
        beat2.SetAllocation(55, "Lite", "Story-beat clip.");
        var still = sb.AddScene(8, "Follow for more.", "closing card", "none", SceneVisualType.AiImage);
        still.SetAllocation(20, null, "CTA: still image.");
        return sb;
    }

    private (FlowGenerationPlanService Service, CreditLedgerService Ledger, FakeAssetReferenceRepository Refs)
        Build(Storyboard? storyboard = null)
    {
        var project = ContentProject.Create("Office Cat", "topic", "storytelling", 60, "9:16", "en");
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        var refs = new FakeAssetReferenceRepository();

        var service = new FlowGenerationPlanService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardRepository(storyboard ?? StoryboardWith(_project)),
            refs,
            ledger,
            Options.Create(new CreditCostOptions()),
            Options.Create(new FlowModelOptions()));

        return (service, ledger, refs);
    }

    [Fact]
    public async Task Plan_lists_only_the_scenes_that_need_flow_and_prices_them()
    {
        var (service, _, _) = Build();

        var plan = await service.BuildAsync(_project);

        Assert.Equal(3, plan.ScenesRequiringFlow);                     // 3 AI_VIDEO, 1 AI_IMAGE
        Assert.Equal(20 + 10 + 10, plan.PlannedCredits);              // Fast + Lite + Lite
        Assert.Equal(50, plan.DailyBudgetCredits);
        Assert.True(plan.WithinBudget);

        var video = plan.Scenes.Where(s => s.GenerationType == "AI_VIDEO").ToList();
        Assert.Equal("Fast", video[0].RecommendedModel);
        Assert.All(video, s => Assert.False(string.IsNullOrWhiteSpace(s.FlowVideoPrompt)));
        Assert.All(video, s => Assert.True(s.EstimatedCredits > 0));
    }

    [Fact]
    public async Task Plan_does_not_force_spending_the_whole_daily_budget()
    {
        var (service, _, _) = Build();

        var plan = await service.BuildAsync(_project);

        Assert.True(plan.PlannedCredits < plan.DailyBudgetCredits); // 40 < 50
    }

    [Fact]
    public async Task An_ai_image_scene_carries_no_flow_model_and_is_not_counted_as_flow_work()
    {
        var (service, _, _) = Build();

        var plan = await service.BuildAsync(_project);
        var stillScene = Assert.Single(plan.Scenes, s => s.GenerationType == "AI_IMAGE");

        Assert.Null(stillScene.RecommendedModel);
        Assert.DoesNotContain(plan.Scenes.Where(s => s.GenerationType == "AI_VIDEO"), s => s.SceneNumber == stillScene.SceneNumber);
    }

    [Fact]
    public async Task Plan_flags_when_it_would_not_fit_the_remaining_flow_credits()
    {
        var (service, ledger, _) = Build();

        // Book 45 credits already spent today -> only 5 remain, plan needs 40.
        await ledger.RecordExternalCompletionAsync(
            new CreditReservationRequest(_project, Guid.NewGuid(), GenerationKind.Video, "google-flow", "google-flow", VideoModelTier.Fast), 45);

        var plan = await service.BuildAsync(_project);

        Assert.Equal(45, plan.UsedCredits);
        Assert.Equal(5, plan.RemainingCredits);
        Assert.Equal(40, plan.PlannedCredits);
        Assert.False(plan.WithinBudget);
    }

    [Fact]
    public async Task Approved_reference_images_are_attached_to_character_scenes()
    {
        var (service, _, refs) = Build();
        var character = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/char.png", null, "upload");
        character.Approve();
        refs.Rows.Add(character);

        var plan = await service.BuildAsync(_project);
        var heroScene = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.True(heroScene.CharacterRequired);
        Assert.Contains(heroScene.ReferenceImageUrls, u => u.Contains(character.Id.ToString()) && u.EndsWith("/file"));
    }

    [Fact]
    public async Task CopyAll_text_bundles_every_flow_prompt_in_the_english_only_export_format()
    {
        var (service, _, _) = Build();

        var plan = await service.BuildAsync(_project);

        Assert.Contains("--- SCENE 1 (Veo Fast - 20 credits) ---", plan.CopyAllText);
        Assert.Contains("--- SCENE 2 (Veo Lite - 10 credits) ---", plan.CopyAllText);
        Assert.Contains("--- SCENE 3 (Veo Lite - 10 credits) ---", plan.CopyAllText);
        Assert.DoesNotContain("SCENE 4", plan.CopyAllText); // the AI_IMAGE scene

        // No Vietnamese instructional text, no fenced "paste from here" markers.
        foreach (var banned in new[] { "RIÊNG BIỆT", "dán từ đây", "đến đây", "Cảnh ", "clip khác nhau", "###", "----" })
        {
            Assert.DoesNotContain(banned, plan.CopyAllText);
        }
    }

    [Fact]
    public async Task CopyAll_text_leaves_out_clips_the_user_already_has_a_video_for()
    {
        var storyboard = StoryboardWith(_project);
        storyboard.Scenes.First().SetSkipGeneration(true); // scene 1 (the Fast hero) already imported

        var (service, _, _) = Build(storyboard);

        var plan = await service.BuildAsync(_project);

        Assert.DoesNotContain("SCENE 1", plan.CopyAllText);
        Assert.Contains("--- SCENE 2 (Veo Lite - 10 credits) ---", plan.CopyAllText);
        Assert.Equal(10 + 10, plan.PlannedCredits);   // the two Lite beats only
        Assert.Equal(2, plan.ScenesRequiringFlow);
    }

    [Fact]
    public async Task Flow_video_prompt_is_one_clean_pasteable_english_paragraph()
    {
        var (service, _, _) = Build();

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");
        var prompt = hero.FlowVideoPrompt;

        // One paragraph, no headings / labels / separators / metadata.
        Assert.DoesNotContain("\n", prompt);
        foreach (var marker in new[] { "Action:", "Camera:", "Style:", "Format:", "Restrictions:", "Camera / motion:", "---", "###" })
        {
            Assert.DoesNotContain(marker, prompt);
        }

        // One deterministic camera move; ends with the vertical format + restriction.
        Assert.Contains("push-in toward the subject.", prompt);
        Assert.DoesNotContain("push-in or handheld drift", prompt);
        Assert.Contains("Vertical 9:16, about 8 seconds.", prompt);
        Assert.EndsWith("No on-screen text, subtitles, captions, logos or watermarks.", prompt);

        // No approved anchors in this Build() -> no consistency sentences.
        Assert.DoesNotContain("character reference", prompt);
        Assert.DoesNotContain("environment reference", prompt);

        // Deterministic: same storyboard -> byte-identical prompt.
        var again = await service.BuildAsync(_project);
        Assert.Equal(prompt, again.Scenes.First(s => s.GenerationType == "AI_VIDEO").FlowVideoPrompt);
    }

    [Fact]
    public async Task Approved_character_and_environment_anchors_add_consistency_sentences()
    {
        var (service, _, refs) = Build();
        foreach (var type in new[] { AssetReferenceType.Character, AssetReferenceType.Environment })
        {
            var r = AssetReference.CreateGenerated(_project, type, $"path/{type}.png", null, "upload");
            r.Approve();
            refs.Rows.Add(r);
        }

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Contains("Keep the main character exactly consistent with the character reference", hero.FlowVideoPrompt);
        Assert.Contains("Keep the setting consistent with the environment reference", hero.FlowVideoPrompt);
        Assert.DoesNotContain("\n", hero.FlowVideoPrompt); // still one paragraph
    }

    [Fact]
    public async Task An_empty_storyboard_yields_an_empty_plan()
    {
        var (service, _, _) = Build(Storyboard.Create(_project));

        var plan = await service.BuildAsync(_project);

        Assert.Empty(plan.Scenes);
        Assert.Equal(0, plan.PlannedCredits);
        Assert.Equal(0, plan.ScenesRequiringFlow);
    }
}
