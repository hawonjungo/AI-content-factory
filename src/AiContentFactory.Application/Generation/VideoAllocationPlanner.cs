using AiContentFactory.Application.Costs;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Generation;

/// <param name="AiVideoPriority">
/// 0-100. How much this scene benefits from real motion. 0 = never worth a
/// video clip (pure exposition); ~100 = the hero moment. Drives which scenes
/// get a clip and which tier.
/// </param>
/// <param name="ImagePriority">0-100. How much this scene benefits from a bespoke still if it isn't getting a clip.</param>
public record AllocationCandidate(
    int SceneNumber,
    int DurationSeconds,
    int AiVideoPriority,
    int ImagePriority);

/// <param name="ModelTier">Set only when <see cref="AssetType"/> is <see cref="SceneVisualType.AiVideo"/>.</param>
public record SceneAllocation(
    int SceneNumber,
    SceneVisualType AssetType,
    VideoModelTier? ModelTier,
    int Credits,
    string Rationale);

public record AllocationPlan(IReadOnlyList<SceneAllocation> Scenes)
{
    public int TotalCredits => Scenes.Sum(s => s.Credits);
    public int VideoCount => Scenes.Count(s => s.AssetType == SceneVisualType.AiVideo);
    public int FastCount => Scenes.Count(s => s.ModelTier == VideoModelTier.Fast);
    public int LiteCount => Scenes.Count(s => s.ModelTier == VideoModelTier.Lite);
    public int ImageCount => Scenes.Count(s => s.AssetType == SceneVisualType.AiImage);
}

public interface IVideoAllocationPlanner
{
    /// <summary>
    /// Decides, for a set of storyboard scenes and a credit budget, which
    /// scenes get an AI video clip (and at which tier) and which get a still.
    /// </summary>
    AllocationPlan Allocate(IReadOnlyList<AllocationCandidate> candidates, int availableCredits, CreditCostOptions costs);
}

/// <summary>
/// Dynamic Fast/Lite/image allocation. The reference plan (1 x Fast + 3 x Lite
/// = 50 credits) is what falls out when there are four strong scenes and a
/// 50-credit budget, but it is not assumed: fewer scenes, weaker scenes, or a
/// smaller budget all produce fewer clips, and AI video is never pinned to a
/// fixed timestamp or a fixed count.
///
/// Rules, in order:
///   * scenes with AiVideoPriority == 0 are always stills - never a clip
///   * the single highest-priority remaining scene gets the Fast (hero) clip,
///     if the budget covers one
///   * remaining scenes, high priority first, get Lite clips while the budget
///     covers them AND their priority clears <see cref="LitePriorityFloor"/>
///   * everything left is an AI still
/// </summary>
public class VideoAllocationPlanner : IVideoAllocationPlanner
{
    /// <summary>A scene must want video at least this much to spend a Lite clip on it.</summary>
    public const int LitePriorityFloor = 40;

    public AllocationPlan Allocate(IReadOnlyList<AllocationCandidate> candidates, int availableCredits, CreditCostOptions costs)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(costs);

        var fastCost = costs.FastVideoCredits;
        var liteCost = costs.LiteVideoCredits;
        var imageCost = costs.ImageCredits;

        var result = new Dictionary<int, SceneAllocation>();
        var budget = Math.Max(0, availableCredits);

        // Priority order: strongest video candidate first, stable on scene number.
        var ranked = candidates
            .OrderByDescending(c => c.AiVideoPriority)
            .ThenBy(c => c.SceneNumber)
            .ToList();

        var fastAssigned = false;

        foreach (var candidate in ranked)
        {
            if (candidate.AiVideoPriority <= 0)
            {
                continue; // handled in the stills pass
            }

            if (!fastAssigned && budget >= fastCost)
            {
                budget -= fastCost;
                fastAssigned = true;
                result[candidate.SceneNumber] = new SceneAllocation(
                    candidate.SceneNumber, SceneVisualType.AiVideo, VideoModelTier.Fast, fastCost,
                    $"Hero clip: highest AI-video priority ({candidate.AiVideoPriority}).");
                continue;
            }

            if (candidate.AiVideoPriority >= LitePriorityFloor && budget >= liteCost)
            {
                budget -= liteCost;
                result[candidate.SceneNumber] = new SceneAllocation(
                    candidate.SceneNumber, SceneVisualType.AiVideo, VideoModelTier.Lite, liteCost,
                    $"Story-beat clip: AI-video priority {candidate.AiVideoPriority}, budget allows a Lite clip.");
                continue;
            }
            // else: falls through to a still below
        }

        // Everything not given a clip becomes an AI still, in original scene order.
        foreach (var candidate in candidates.OrderBy(c => c.SceneNumber))
        {
            if (result.ContainsKey(candidate.SceneNumber))
            {
                continue;
            }

            var reason = candidate.AiVideoPriority <= 0
                ? "Exposition scene: no motion value, still image."
                : budget < liteCost
                    ? "Budget exhausted for clips: still image."
                    : $"AI-video priority {candidate.AiVideoPriority} below the Lite floor ({LitePriorityFloor}): still image.";

            result[candidate.SceneNumber] = new SceneAllocation(
                candidate.SceneNumber, SceneVisualType.AiImage, null, imageCost, reason);
        }

        var ordered = result.Values.OrderBy(s => s.SceneNumber).ToList();
        return new AllocationPlan(ordered);
    }
}
