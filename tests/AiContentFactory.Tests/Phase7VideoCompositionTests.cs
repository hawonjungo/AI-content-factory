using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Phase 7 - per-clip model selection, accurate per-tier cost, duplicate-generation
/// protection, and the English-only Flow export.
/// </summary>
public class Phase7VideoCompositionTests
{
    private readonly Guid _project = Guid.NewGuid();

    // ---- 1. Per-clip model tier is a property of the individual clip ----------

    [Fact]
    public void SetModelTier_overrides_only_the_tier_not_the_allocator_priority_or_rationale()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "n", "shot", "push", SceneVisualType.AiVideo);
        scene.SetAllocation(70, "Lite", "story-beat clip");

        scene.SetModelTier("Fast");

        Assert.Equal("Fast", scene.ModelTier);
        Assert.Equal(70, scene.AiVideoPriority);
        Assert.Equal("story-beat clip", scene.AllocationRationale);

        scene.SetModelTier("  ");
        Assert.Null(scene.ModelTier); // blank restores "use the allocator's pick"
    }

    [Fact]
    public void Two_clips_can_carry_different_tiers_independently()
    {
        var sb = Storyboard.Create(_project);
        var a = sb.AddScene(8, "a", "shot", "push", SceneVisualType.AiVideo);
        var b = sb.AddScene(8, "b", "shot", "pan", SceneVisualType.AiVideo);

        a.SetModelTier("Fast");
        b.SetModelTier("Lite");

        Assert.Equal("Fast", sb.Scenes.First(s => s.Id == a.Id).ModelTier);
        Assert.Equal("Lite", sb.Scenes.First(s => s.Id == b.Id).ModelTier);
    }

    // ---- 2. Accurate cost: total = sum of each clip's own tier price ---------

    [Fact]
    public void Pricing_resolves_a_different_usd_per_second_per_tier()
    {
        var pricing = new PricingOptions();

        // ~$3.20 / 8s Fast, ~$0.64 / 8s Lite.
        Assert.Equal(3.20m, pricing.VideoUsdPerSecondFor(VideoModelTier.Fast) * 8);
        Assert.Equal(0.64m, pricing.VideoUsdPerSecondFor(VideoModelTier.Lite) * 8);
    }

    [Fact]
    public async Task Estimate_totals_each_clip_at_its_own_tier_not_one_global_price()
    {
        var sb = Storyboard.Create(_project);
        var fast = sb.AddScene(8, "hero", "shot", "push", SceneVisualType.AiVideo);
        fast.SetAllocation(100, "Fast", "hero");
        var lite = sb.AddScene(8, "beat", "shot", "pan", SceneVisualType.AiVideo);
        lite.SetAllocation(60, "Lite", "beat");

        var estimator = BuildEstimator(sb);

        var estimate = await estimator.EstimateProjectAsync(_project);

        // 8s * $0.40 (Fast) + 8s * $0.08 (Lite) = 3.20 + 0.64 = 3.84
        Assert.Equal(3.84m, estimate.TotalCostUsd);
    }

    [Fact]
    public async Task A_skipped_clip_is_excluded_from_the_cost_estimate()
    {
        var sb = Storyboard.Create(_project);
        var fast = sb.AddScene(8, "hero", "shot", "push", SceneVisualType.AiVideo);
        fast.SetAllocation(100, "Fast", "hero");
        var lite = sb.AddScene(8, "beat", "shot", "pan", SceneVisualType.AiVideo);
        lite.SetAllocation(60, "Lite", "beat");
        lite.SetSkipGeneration(true); // user already has this clip

        var estimator = BuildEstimator(sb);

        var estimate = await estimator.EstimateProjectAsync(_project);

        Assert.Equal(3.20m, estimate.TotalCostUsd);       // only the Fast clip is priced
        Assert.Equal(1, estimate.AlreadyGeneratedClips);  // the skipped clip counts as done
    }

    // ---- 3. Duplicate-generation protection (backend is the authority) -------

    [Fact]
    public async Task SceneAssetGenerator_never_calls_a_provider_for_a_skipped_clip()
    {
        var video = new FakeVideoGenerationProvider();
        var image = new FakeImageGenerationProvider();
        var generator = new SceneAssetGenerator(
            storyboardService: null!,
            assetService: null!,
            promptAgent: null!,
            videoProvider: video,
            imageProvider: image,
            ttsService: null!,
            audioTiming: null!,
            assetReferenceService: null!,
            quotaManager: null!,
            creditLedger: null!,
            fileStorage: null!,
            usageTracker: null!,
            storyVisualContextResolver: null!,
            pricing: Options.Create(new PricingOptions()),
            creditCosts: Options.Create(new CreditCostOptions()),
            flow: Options.Create(new GoogleFlowOptions()),
            videoOptions: Options.Create(new VideoGenerationOptions()),
            logger: NullLogger<SceneAssetGenerator>.Instance);

        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "n", "shot", "push", SceneVisualType.AiVideo);
        scene.SetSkipGeneration(true);
        var context = new SceneGenerationContext(_project, "9:16", null!, null!, null, null, Array.Empty<ApprovedSceneReference>());

        await generator.GenerateClipAsync(context, SceneResponse.FromDomain(scene), refreshPrompt: false);

        Assert.Equal(0, video.Calls);
        Assert.Equal(0, image.Calls);
    }

    [Fact]
    public async Task SceneAssetGenerator_refuses_an_unsupported_visual_type_before_touching_budget_or_a_provider()
    {
        var video = new FakeVideoGenerationProvider();
        var image = new FakeImageGenerationProvider();
        var usageTracker = new FakeAiUsageTracker();
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);

        var generator = new SceneAssetGenerator(
            storyboardService: null!,
            assetService: null!,
            promptAgent: null!,
            videoProvider: video,
            imageProvider: image,
            ttsService: null!,
            audioTiming: null!,
            assetReferenceService: null!,
            quotaManager: null!,
            creditLedger: ledger,
            fileStorage: null!,
            usageTracker: usageTracker,
            storyVisualContextResolver: null!,
            pricing: Options.Create(new PricingOptions()),
            creditCosts: Options.Create(new CreditCostOptions()),
            flow: Options.Create(new GoogleFlowOptions()),
            videoOptions: Options.Create(new VideoGenerationOptions()),
            logger: NullLogger<SceneAssetGenerator>.Instance);

        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "n", "shot", "push", SceneVisualType.MotionGraphic);
        var context = new SceneGenerationContext(_project, "9:16", null!, null!, null, null, Array.Empty<ApprovedSceneReference>());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => generator.GenerateClipAsync(context, SceneResponse.FromDomain(scene), refreshPrompt: false));

        Assert.Equal(0, video.Calls);
        Assert.Equal(0, image.Calls);
        Assert.Empty(usageTracker.Records);
        var usage = await ledger.GetDailyUsageAsync();
        Assert.Equal(0, usage.Reserved);
        Assert.Equal(0, usage.Used);
    }

    [Fact]
    public async Task ClipRegeneration_refuses_a_skipped_clip_and_never_starts_a_job()
    {
        var project = ContentProject.Create("t", "topic", "storytelling", 60, "9:16", "en");
        var sb = Storyboard.Create(project.Id);
        var scene = sb.AddScene(8, "n", "shot", "push", SceneVisualType.AiVideo);
        scene.SetSkipGeneration(true);

        var repo = new FakeStoryboardRepository(sb);
        var spy = new SpySceneAssetGenerator();
        var service = new ClipRegenerationService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardService(repo),
            spy,
            NullLogger<ClipRegenerationService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RunAsync(project.Id, scene.Id, new RegenerateClipRequest()));

        Assert.Empty(spy.ClipCalls);
        Assert.Empty(spy.VoiceCalls);
    }

    [Fact]
    public async Task Full_run_skips_generation_for_a_skipped_clip_but_still_makes_its_voice_over()
    {
        var project = ContentProject.Create("t", "topic", "storytelling", 60, "9:16", "en");
        project.SetAudioMode(AudioMode.Generated); // this test covers the generated-voice pipeline
        var sb = Storyboard.Create(project.Id);
        var normal = sb.AddScene(8, "one", "shot", "push", SceneVisualType.AiVideo);
        var skipped = sb.AddScene(8, "two", "shot", "pan", SceneVisualType.AiVideo);
        skipped.SetSkipGeneration(true);

        var repo = new FakeStoryboardRepository(sb);
        var spy = new SpySceneAssetGenerator();
        var service = new AssetGenerationService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardService(repo),
            new FakeAssetService(),
            spy,
            NullLogger<AssetGenerationService>.Instance);

        await service.RunAsync(project.Id);

        Assert.Contains(normal.Id, spy.ClipCalls);
        Assert.DoesNotContain(skipped.Id, spy.ClipCalls);   // no generation request / job
        Assert.Contains(skipped.Id, spy.VoiceCalls);        // narration is still mandatory
    }

    [Fact]
    public async Task Full_run_generates_no_TTS_when_the_audio_mode_is_not_generated()
    {
        var project = ContentProject.Create("t", "topic", "storytelling", 60, "9:16", "en");
        // Smart defers per-clip TTS to render time, so asset generation makes none.
        project.SetAudioMode(AudioMode.Smart);

        var sb = Storyboard.Create(project.Id);
        sb.AddScene(8, "one", "shot", "push", SceneVisualType.AiVideo);
        sb.AddScene(8, "two", "shot", "pan", SceneVisualType.AiVideo);

        var repo = new FakeStoryboardRepository(sb);
        var spy = new SpySceneAssetGenerator();
        var service = new AssetGenerationService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardService(repo),
            new FakeAssetService(),
            spy,
            NullLogger<AssetGenerationService>.Instance);

        await service.RunAsync(project.Id);

        Assert.Equal(2, spy.ClipCalls.Count);   // visuals are still generated
        Assert.Empty(spy.VoiceCalls);           // ...but not a single TTS request
    }

    // ---- 4. Google Flow export is English only ------------------------------

    [Fact]
    public async Task Flow_export_uses_the_english_scene_header_format_and_no_vietnamese_instructions()
    {
        var sb = Storyboard.Create(_project);
        var hero = sb.AddScene(8, "The cat walked in.", "wide shot", "slow push in", SceneVisualType.AiVideo);
        hero.SetAllocation(100, "Fast", "hero");
        var beat = sb.AddScene(8, "It joined the meeting.", "cat on chair", "handheld", SceneVisualType.AiVideo);
        beat.SetAllocation(60, "Lite", "beat");

        var plan = await BuildFlowPlan(sb).BuildAsync(_project);

        Assert.Contains("--- SCENE 1 (Veo Fast - 20 credits) ---", plan.CopyAllText);
        Assert.Contains("--- SCENE 2 (Veo Lite - 10 credits) ---", plan.CopyAllText);

        foreach (var vietnamese in new[]
                 {
                     "RIÊNG BIỆT", "dán từ đây", "đến đây", "Cảnh ", "clip khác nhau", "###",
                 })
        {
            Assert.DoesNotContain(vietnamese, plan.CopyAllText);
        }
    }

    [Fact]
    public async Task Flow_export_leaves_out_clips_the_user_already_has_a_video_for()
    {
        var sb = Storyboard.Create(_project);
        var hero = sb.AddScene(8, "Hero.", "wide", "push", SceneVisualType.AiVideo);
        hero.SetAllocation(100, "Fast", "hero");
        var done = sb.AddScene(8, "Already done.", "shot", "pan", SceneVisualType.AiVideo);
        done.SetAllocation(60, "Lite", "beat");
        done.SetSkipGeneration(true);

        var plan = await BuildFlowPlan(sb).BuildAsync(_project);

        Assert.Contains("--- SCENE 1 (Veo Fast - 20 credits) ---", plan.CopyAllText);
        Assert.DoesNotContain("SCENE 2", plan.CopyAllText);
        Assert.Equal(20, plan.PlannedCredits);      // Fast only - the skipped Lite is not planned
        Assert.Equal(1, plan.ScenesRequiringFlow);
    }

    // ---- harness -----------------------------------------------------------

    private GenerationEstimator BuildEstimator(Storyboard storyboard)
    {
        var repo = new FakeStoryboardRepository(storyboard);
        return new GenerationEstimator(
            new FakeStoryboardService(repo),
            new FakeAssetService(),
            new FakeAiUsageTracker(),
            new FakeGoogleFlowQuotaManager(),
            Options.Create(new PricingOptions()),
            Options.Create(new BudgetOptions()),
            Options.Create(new GoogleFlowOptions()),
            NullLogger<GenerationEstimator>.Instance);
    }

    private FlowGenerationPlanService BuildFlowPlan(Storyboard storyboard)
    {
        var project = ContentProject.Create("Office Cat", "topic", "storytelling", 60, "9:16", "en");
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);

        return new FlowGenerationPlanService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardRepository(storyboard),
            new FakeAssetReferenceRepository(),
            ledger,
            new FakeStoryVisualContextResolver(),
            Options.Create(new CreditCostOptions()),
            Options.Create(new FlowModelOptions()));
    }
}
