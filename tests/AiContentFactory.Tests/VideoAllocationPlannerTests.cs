using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using Xunit;

namespace AiContentFactory.Tests;

public class VideoAllocationPlannerTests
{
    private readonly VideoAllocationPlanner _planner = new();
    private readonly CreditCostOptions _costs = new(); // 50 budget, Fast 20, Lite 10

    private static AllocationCandidate Scene(int n, int priority, int duration = 8) => new(n, duration, priority, 60);

    [Fact]
    public void Four_strong_scenes_on_a_50_credit_budget_yield_one_fast_and_three_lite()
    {
        var candidates = new[]
        {
            Scene(1, 100), // hero
            Scene(2, 60),
            Scene(3, 80),
            Scene(4, 50)
        };

        var plan = _planner.Allocate(candidates, availableCredits: 50, _costs);

        Assert.Equal(1, plan.FastCount);
        Assert.Equal(3, plan.LiteCount);
        Assert.Equal(0, plan.ImageCount);
        Assert.Equal(50, plan.TotalCredits);

        // Fast goes to the single highest-priority scene, not a fixed slot.
        Assert.Equal(VideoModelTier.Fast, plan.Scenes.Single(s => s.SceneNumber == 1).ModelTier);
    }

    [Fact]
    public void Allocation_is_dynamic_not_fixed_at_four_clips()
    {
        var candidates = Enumerable.Range(1, 6).Select(n => Scene(n, n == 1 ? 100 : 70)).ToList();

        var plan = _planner.Allocate(candidates, availableCredits: 50, _costs);

        // 20 (fast) + 3x10 (lite) = 50; the remaining two scenes are stills.
        Assert.Equal(4, plan.VideoCount);
        Assert.Equal(2, plan.ImageCount);
        Assert.True(plan.TotalCredits <= 50);
    }

    [Fact]
    public void A_small_budget_buys_fewer_clips()
    {
        var candidates = new[] { Scene(1, 100), Scene(2, 80), Scene(3, 70), Scene(4, 60) };

        var plan = _planner.Allocate(candidates, availableCredits: 20, _costs);

        Assert.Equal(1, plan.FastCount);
        Assert.Equal(0, plan.LiteCount);
        Assert.Equal(3, plan.ImageCount);
    }

    [Fact]
    public void Zero_priority_scenes_never_get_a_clip_even_with_budget_to_spare()
    {
        var candidates = new[] { Scene(1, 100), Scene(2, 0), Scene(3, 0) };

        var plan = _planner.Allocate(candidates, availableCredits: 50, _costs);

        Assert.Equal(SceneVisualType.AiImage, plan.Scenes.Single(s => s.SceneNumber == 2).AssetType);
        Assert.Equal(SceneVisualType.AiImage, plan.Scenes.Single(s => s.SceneNumber == 3).AssetType);
        Assert.Equal(1, plan.VideoCount);
    }

    [Fact]
    public void Scenes_below_the_lite_priority_floor_stay_stills()
    {
        var candidates = new[] { Scene(1, 100), Scene(2, 30) }; // 30 < LitePriorityFloor (40)

        var plan = _planner.Allocate(candidates, availableCredits: 50, _costs);

        Assert.Equal(SceneVisualType.AiImage, plan.Scenes.Single(s => s.SceneNumber == 2).AssetType);
    }

    [Fact]
    public void The_hero_clip_follows_priority_not_scene_order()
    {
        var candidates = new[] { Scene(1, 30), Scene(2, 90), Scene(3, 20) };

        var plan = _planner.Allocate(candidates, availableCredits: 20, _costs);

        Assert.Equal(VideoModelTier.Fast, plan.Scenes.Single(s => s.SceneNumber == 2).ModelTier);
        Assert.Null(plan.Scenes.Single(s => s.SceneNumber == 1).ModelTier);
    }

    [Fact]
    public void Credit_costs_are_taken_from_configuration()
    {
        var costs = new CreditCostOptions { FastVideoCredits = 25, LiteVideoCredits = 5, DailyBudgetCredits = 40 };
        var candidates = new[] { Scene(1, 100), Scene(2, 80) };

        var plan = _planner.Allocate(candidates, availableCredits: 40, costs);

        Assert.Equal(25, plan.Scenes.Single(s => s.SceneNumber == 1).Credits);
        Assert.Equal(5, plan.Scenes.Single(s => s.SceneNumber == 2).Credits);
        Assert.Equal(30, plan.TotalCredits);
    }
}
