using System.Text.RegularExpressions;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Storyboards;

/// <summary>The job a scene does in the narrative - drives its priorities and duration weight.</summary>
public enum ScenePurpose
{
    Hook = 0,
    Context = 1,
    Beat = 2,
    Climax = 3,
    Payoff = 4,
    CallToAction = 5
}

/// <summary>The six script sections a plan is built from (see <see cref="AiContentFactory.Domain.Scripts.Script"/>).</summary>
public record ScriptSections(
    string Hook,
    string Introduction,
    string Body,
    string Escalation,
    string Payoff,
    string CallToAction);

/// <param name="NarrationSegment">What the narrator says for this scene (goes to TTS).</param>
/// <param name="CaptionText">On-screen text for this scene - equal to the narration by default, editable later.</param>
/// <param name="AiVideoPriority">0-100, how much the scene benefits from real motion.</param>
/// <param name="ImagePriority">0-100, how much a bespoke still is worth if it isn't getting a clip.</param>
public record PlannedScene(
    int SceneNumber,
    ScenePurpose Purpose,
    string NarrativeBeat,
    string NarrationSegment,
    string CaptionText,
    string VisualDescription,
    int EstimatedDurationSeconds,
    int AiVideoPriority,
    int ImagePriority,
    bool RequiresCharacterReference,
    bool RequiresEnvironmentReference,
    SceneVisualType AssetType,
    VideoModelTier? ModelTier,
    int EstimatedCredits,
    string AllocationRationale);

public record StoryboardPlan(
    Guid ContentProjectId,
    IReadOnlyList<PlannedScene> Scenes,
    int TotalDurationSeconds,
    int TotalEstimatedCredits)
{
    public int VideoCount => Scenes.Count(s => s.AssetType == SceneVisualType.AiVideo);
    public int FastCount => Scenes.Count(s => s.ModelTier == VideoModelTier.Fast);
    public int LiteCount => Scenes.Count(s => s.ModelTier == VideoModelTier.Lite);
    public int ImageCount => Scenes.Count(s => s.AssetType == SceneVisualType.AiImage);
}

public interface IStoryboardPlanner
{
    /// <summary>
    /// Turns a script into a scene-by-scene plan: purpose, narrative beat,
    /// narration segment, visual description, estimated duration, AI-video and
    /// image priorities, continuity/reference requirements, and - via the
    /// allocation planner - which scenes get an AI clip and at which tier.
    /// Total duration is steered into the 55-75s target window; it never
    /// produces a single continuous long clip.
    /// </summary>
    StoryboardPlan Plan(Guid contentProjectId, ScriptSections script, int targetDurationSeconds, int availableCredits, CreditCostOptions costs);
}

public class StoryboardPlanner : IStoryboardPlanner
{
    public const int TargetMinSeconds = 55;
    public const int TargetMaxSeconds = 75;
    public const int MinSceneSeconds = 4;
    public const int MaxSceneSeconds = 10;

    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly IVideoAllocationPlanner _allocationPlanner;

    public StoryboardPlanner(IVideoAllocationPlanner allocationPlanner)
    {
        _allocationPlanner = allocationPlanner;
    }

    public StoryboardPlan Plan(Guid contentProjectId, ScriptSections script, int targetDurationSeconds, int availableCredits, CreditCostOptions costs)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(costs);

        var beats = BuildBeats(script);
        if (beats.Count == 0)
        {
            return new StoryboardPlan(contentProjectId, Array.Empty<PlannedScene>(), 0, 0);
        }

        var durations = DistributeDurations(beats, targetDurationSeconds);

        var candidates = beats
            .Select((beat, i) => new AllocationCandidate(i + 1, durations[i], beat.AiVideoPriority, beat.ImagePriority))
            .ToList();

        var allocation = _allocationPlanner.Allocate(candidates, availableCredits, costs);
        var bySceneNumber = allocation.Scenes.ToDictionary(s => s.SceneNumber);

        var scenes = new List<PlannedScene>(beats.Count);
        for (var i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            var sceneNumber = i + 1;
            var slot = bySceneNumber[sceneNumber];

            scenes.Add(new PlannedScene(
                SceneNumber: sceneNumber,
                Purpose: beat.Purpose,
                NarrativeBeat: beat.NarrativeBeat,
                NarrationSegment: beat.Text,
                CaptionText: beat.Text,
                VisualDescription: BuildVisualDescription(beat),
                EstimatedDurationSeconds: durations[i],
                AiVideoPriority: beat.AiVideoPriority,
                ImagePriority: beat.ImagePriority,
                RequiresCharacterReference: beat.RequiresCharacterReference,
                RequiresEnvironmentReference: beat.RequiresEnvironmentReference,
                AssetType: slot.AssetType,
                ModelTier: slot.ModelTier,
                EstimatedCredits: slot.Credits,
                AllocationRationale: slot.Rationale));
        }

        return new StoryboardPlan(
            contentProjectId,
            scenes,
            scenes.Sum(s => s.EstimatedDurationSeconds),
            scenes.Sum(s => s.EstimatedCredits));
    }

    private sealed record Beat(
        ScenePurpose Purpose,
        string NarrativeBeat,
        string Text,
        int AiVideoPriority,
        int ImagePriority,
        bool RequiresCharacterReference,
        bool RequiresEnvironmentReference);

    private static List<Beat> BuildBeats(ScriptSections script)
    {
        var beats = new List<Beat>();

        AddIfPresent(beats, ScenePurpose.Hook, "Cold-open hook", script.Hook,
            aiVideoPriority: 100, imagePriority: 60, character: true, environment: true);

        AddIfPresent(beats, ScenePurpose.Context, "Set-up / context", script.Introduction,
            aiVideoPriority: 30, imagePriority: 70, character: false, environment: true);

        // The body is usually the longest section - split it into up to three
        // story beats so it doesn't become one over-long scene.
        foreach (var (chunk, index) in SplitBody(script.Body).Select((c, i) => (c, i)))
        {
            AddIfPresent(beats, ScenePurpose.Beat, $"Story beat {index + 1}", chunk,
                aiVideoPriority: 55, imagePriority: 65, character: true, environment: false);
        }

        AddIfPresent(beats, ScenePurpose.Climax, "Peak / turn", script.Escalation,
            aiVideoPriority: 85, imagePriority: 55, character: true, environment: true);

        AddIfPresent(beats, ScenePurpose.Payoff, "Payoff", script.Payoff,
            aiVideoPriority: 70, imagePriority: 60, character: true, environment: false);

        AddIfPresent(beats, ScenePurpose.CallToAction, "Call to action", script.CallToAction,
            aiVideoPriority: 15, imagePriority: 50, character: false, environment: false);

        return beats;
    }

    private static void AddIfPresent(
        List<Beat> beats,
        ScenePurpose purpose,
        string narrativeBeat,
        string? text,
        int aiVideoPriority,
        int imagePriority,
        bool character,
        bool environment)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        beats.Add(new Beat(purpose, narrativeBeat, text.Trim(), aiVideoPriority, imagePriority, character, environment));
    }

    private static IEnumerable<string> SplitBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            yield break;
        }

        var sentences = SentenceSplit.Split(body.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        if (sentences.Count <= 1)
        {
            yield return body.Trim();
            yield break;
        }

        // At most three beats, sentences spread as evenly as possible.
        var beatCount = Math.Min(3, sentences.Count);
        var perBeat = (int)Math.Ceiling(sentences.Count / (double)beatCount);
        for (var i = 0; i < sentences.Count; i += perBeat)
        {
            yield return string.Join(" ", sentences.Skip(i).Take(perBeat));
        }
    }

    /// <summary>
    /// Weight each beat by narration length, scale the weights to the target
    /// total (clamped into the 55-75s window), clamp each scene to 4-10s, then
    /// hand any rounding remainder back out a second at a time.
    /// </summary>
    private static int[] DistributeDurations(IReadOnlyList<Beat> beats, int targetDurationSeconds)
    {
        var n = beats.Count;

        var floor = Math.Max(TargetMinSeconds, n * MinSceneSeconds);
        var ceiling = Math.Min(TargetMaxSeconds, n * MaxSceneSeconds);
        if (ceiling < floor)
        {
            // Very few beats: the 55-75s window is out of reach - aim for the
            // most the scenes can hold.
            floor = ceiling;
        }

        var target = targetDurationSeconds <= 0
            ? (floor + ceiling) / 2
            : Math.Clamp(targetDurationSeconds, floor, ceiling);

        var weights = beats.Select(b => (double)Math.Max(1, b.Text.Length)).ToArray();
        var totalWeight = weights.Sum();

        var durations = new int[n];
        for (var i = 0; i < n; i++)
        {
            var raw = (int)Math.Round(target * (weights[i] / totalWeight));
            durations[i] = Math.Clamp(raw, MinSceneSeconds, MaxSceneSeconds);
        }

        // Nudge the total onto the target without breaking the per-scene clamp.
        var diff = target - durations.Sum();
        for (var guard = 0; diff != 0 && guard < 10_000; guard++)
        {
            var moved = false;
            for (var i = 0; i < n && diff != 0; i++)
            {
                if (diff > 0 && durations[i] < MaxSceneSeconds)
                {
                    durations[i]++;
                    diff--;
                    moved = true;
                }
                else if (diff < 0 && durations[i] > MinSceneSeconds)
                {
                    durations[i]--;
                    diff++;
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return durations;
    }

    /// <summary>
    /// Placeholder shown before anyone edits the scene or runs "Suggest prompt". Deliberately
    /// describes an on-screen action rather than quoting the narration - the narration is TTS
    /// text, not something an image/video model can "draw", so it must never end up here.
    /// </summary>
    private static string BuildVisualDescription(Beat beat)
    {
        var (label, action) = beat.Purpose switch
        {
            ScenePurpose.Hook => ("Establishing hero shot", "Character enters or reacts sharply to grab attention; camera pushes in slowly."),
            ScenePurpose.Context => ("Wide context shot", "Character going about the everyday moment; camera holds steady in a wide frame."),
            ScenePurpose.Beat => ("Story-beat shot", "Character performs a clear physical action tied to this beat, visible expression."),
            ScenePurpose.Climax => ("High-impact climax shot", "Character reacts intensely - sudden movement, dramatic light shift, fast camera motion."),
            ScenePurpose.Payoff => ("Payoff shot", "Character's expression shifts to relief or resolution as the outcome lands."),
            ScenePurpose.CallToAction => ("Closing shot", "Character faces the camera directly with an inviting gesture; slow zoom in."),
            _ => ("Shot", "Character performs a clear, deliberate action matching the scene.")
        };
        return $"{label}. {action}";
    }
}
