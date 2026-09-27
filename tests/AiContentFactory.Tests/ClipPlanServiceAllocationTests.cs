using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class ClipPlanServiceAllocationTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static ScriptResponse Script() => new(
        Guid.NewGuid(), Guid.NewGuid(),
        Hook: "A cat walked into the office.",
        Introduction: "Nobody noticed for three days.",
        Body: "It slept on a laptop. It joined the standup. It fixed a bug by staring at it.",
        Escalation: "Then the investors came to visit.",
        Payoff: "The round closed that afternoon.",
        CallToAction: "Follow for more.",
        DateTimeOffset.UtcNow);

    private ClipPlanService Build(CreditStrategy strategy, IStoryVisualContextResolver? storyVisualContextResolver = null)
    {
        var project = ContentProject.Create("t", "topic", "niche", 60, "9:16", "en");
        project.UpdateIdeaConfig(ContentIdeaConfig.Create(
            null, null, null, null, null, VoiceGender.Unspecified, null, null, null, strategy));

        return new ClipPlanService(
            new FakeScriptService(Script()),
            new FakeStoryboardRepository(Storyboard.Create(_project)),
            new FakeContentProjectRepository(project),
            new VideoAllocationPlanner(),
            storyVisualContextResolver ?? new FakeStoryVisualContextResolver(),
            Options.Create(new CreditCostOptions())); // 50 budget, Fast 20, Lite 10
    }

    private async Task<IReadOnlyList<SceneResponse>> Plan(CreditStrategy strategy, int clipCount = 6)
    {
        var storyboard = await Build(strategy).GenerateAsync(_project, new GenerateClipPlanRequest(clipCount, 8));
        return storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();
    }

    [Fact]
    public async Task Balanced_strategy_produces_the_reference_1_fast_plus_3_lite_shape()
    {
        var scenes = await Plan(CreditStrategy.Balanced, clipCount: 6);

        var video = scenes.Where(s => s.VisualType == nameof(SceneVisualType.AiVideo)).ToList();
        Assert.Equal("Fast", scenes[0].ModelTier);            // hero
        Assert.Equal(4, video.Count);                          // 1 Fast + 3 Lite
        Assert.Equal(3, video.Count(s => s.ModelTier == "Lite"));
        Assert.Equal(2, scenes.Count(s => s.VisualType == nameof(SceneVisualType.AiImage)));
    }

    [Fact]
    public async Task Every_scene_gets_a_priority_and_a_reason()
    {
        var scenes = await Plan(CreditStrategy.Balanced);

        Assert.Equal(100, scenes[0].AiVideoPriority);          // hook is the hero
        Assert.All(scenes, s => Assert.False(string.IsNullOrWhiteSpace(s.AllocationRationale)));
        Assert.True(scenes[^1].AiVideoPriority < scenes[0].AiVideoPriority); // CTA lower than hook
    }

    [Fact]
    public async Task Economy_strategy_puts_video_only_on_the_hero_scene()
    {
        var scenes = await Plan(CreditStrategy.Economy, clipCount: 6);

        Assert.Equal(nameof(SceneVisualType.AiVideo), scenes[0].VisualType);
        Assert.All(scenes.Skip(1), s => Assert.Equal(nameof(SceneVisualType.AiImage), s.VisualType));
    }

    [Fact]
    public async Task MaxImpact_strategy_uses_video_on_every_scene_the_budget_allows()
    {
        var scenes = await Plan(CreditStrategy.MaxImpact, clipCount: 4);

        // 20 (Fast) + 3x10 (Lite) = 50 -> all four scenes are video.
        Assert.All(scenes, s => Assert.Equal(nameof(SceneVisualType.AiVideo), s.VisualType));
    }

    [Fact]
    public async Task An_explicit_request_strategy_overrides_the_project_credit_strategy()
    {
        var storyboard = await Build(CreditStrategy.Economy)
            .GenerateAsync(_project, new GenerateClipPlanRequest(4, 8, ClipPlanStrategy.AllVideo));
        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();

        Assert.All(scenes, s => Assert.Equal(nameof(SceneVisualType.AiVideo), s.VisualType));
    }
}
