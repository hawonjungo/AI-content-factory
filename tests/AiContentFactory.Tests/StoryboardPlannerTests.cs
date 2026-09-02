using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryboardPlannerTests
{
    private readonly StoryboardPlanner _planner = new(new VideoAllocationPlanner());
    private readonly CreditCostOptions _costs = new();

    private static ScriptSections FullScript() => new(
        Hook: "A cat walked into the office and nobody noticed for three days.",
        Introduction: "This is the story of how a stray tabby became the most productive member of a startup.",
        Body: "It started on a rainy Monday. The cat slipped through an open door and curled up on a warm laptop. " +
              "By Wednesday it had its own chair. Engineers began explaining bugs to it out loud, and the bugs got fixed. " +
              "Someone added it to the standup rotation as a joke, and the joke stuck.",
        Escalation: "Then the investors visited, and the cat walked across the pitch deck at the exact right moment.",
        Payoff: "The round closed that afternoon. The cat got a title and a dental plan.",
        CallToAction: "Follow for more workplace disasters that somehow worked out.");

    [Fact]
    public void Total_duration_lands_in_the_55_to_75_second_window()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), targetDurationSeconds: 65, availableCredits: 50, _costs);

        Assert.InRange(plan.TotalDurationSeconds, StoryboardPlanner.TargetMinSeconds, StoryboardPlanner.TargetMaxSeconds);
        Assert.All(plan.Scenes, s => Assert.InRange(
            s.EstimatedDurationSeconds, StoryboardPlanner.MinSceneSeconds, StoryboardPlanner.MaxSceneSeconds));
    }

    [Fact]
    public void An_out_of_range_target_is_pulled_into_the_window()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), targetDurationSeconds: 600, availableCredits: 50, _costs);

        Assert.InRange(plan.TotalDurationSeconds, StoryboardPlanner.TargetMinSeconds, StoryboardPlanner.TargetMaxSeconds);
    }

    [Fact]
    public void It_does_not_emit_one_continuous_long_clip()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), 65, 50, _costs);

        Assert.True(plan.Scenes.Count >= 4);
        Assert.All(plan.Scenes, s => Assert.True(s.EstimatedDurationSeconds <= StoryboardPlanner.MaxSceneSeconds));
    }

    [Fact]
    public void The_hook_is_the_hero_and_gets_the_fast_tier()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), 65, availableCredits: 50, _costs);

        var hook = plan.Scenes.First();
        Assert.Equal(ScenePurpose.Hook, hook.Purpose);
        Assert.Equal(100, hook.AiVideoPriority);
        Assert.Equal(SceneVisualType.AiVideo, hook.AssetType);
        Assert.Equal(VideoModelTier.Fast, hook.ModelTier);
    }

    [Fact]
    public void Default_allocation_matches_the_reference_1_fast_plus_3_lite_shape_when_budget_allows()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), 65, availableCredits: 50, _costs);

        Assert.Equal(1, plan.FastCount);
        Assert.Equal(3, plan.LiteCount);
        Assert.True(plan.TotalEstimatedCredits <= 50);
    }

    [Fact]
    public void Hook_requires_both_character_and_environment_continuity()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), 65, 50, _costs);

        var hook = plan.Scenes.First();
        Assert.True(hook.RequiresCharacterReference);
        Assert.True(hook.RequiresEnvironmentReference);
    }

    [Fact]
    public void Empty_script_sections_are_skipped()
    {
        var sparse = new ScriptSections(
            Hook: "One line hook.",
            Introduction: "",
            Body: "A single body sentence.",
            Escalation: "   ",
            Payoff: "The payoff.",
            CallToAction: "");

        var plan = _planner.Plan(Guid.NewGuid(), sparse, 60, 50, _costs);

        Assert.Equal(3, plan.Scenes.Count);
        Assert.Contains(plan.Scenes, s => s.Purpose == ScenePurpose.Hook);
        Assert.Contains(plan.Scenes, s => s.Purpose == ScenePurpose.Beat);
        Assert.Contains(plan.Scenes, s => s.Purpose == ScenePurpose.Payoff);
        Assert.DoesNotContain(plan.Scenes, s => s.Purpose == ScenePurpose.Context);
    }

    [Fact]
    public void Caption_text_defaults_to_the_narration_segment()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), 65, 50, _costs);

        Assert.All(plan.Scenes, s => Assert.Equal(s.NarrationSegment, s.CaptionText));
        Assert.All(plan.Scenes, s => Assert.False(string.IsNullOrWhiteSpace(s.NarrationSegment)));
    }

    [Fact]
    public void A_tight_budget_produces_a_valid_plan_with_fewer_clips()
    {
        var plan = _planner.Plan(Guid.NewGuid(), FullScript(), 65, availableCredits: 10, _costs);

        Assert.InRange(plan.TotalDurationSeconds, StoryboardPlanner.TargetMinSeconds, StoryboardPlanner.TargetMaxSeconds);
        Assert.True(plan.VideoCount <= 1);
        Assert.True(plan.TotalEstimatedCredits <= 10);
        Assert.Contains(plan.Scenes, s => s.AssetType == SceneVisualType.AiImage);
    }

    [Fact]
    public void Empty_script_yields_an_empty_plan()
    {
        var plan = _planner.Plan(Guid.NewGuid(), new ScriptSections("", "", "", "", "", ""), 65, 50, _costs);

        Assert.Empty(plan.Scenes);
        Assert.Equal(0, plan.TotalDurationSeconds);
    }
}
