using System.Text.RegularExpressions;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Storyboards;

public enum ClipPlanStrategy
{
    /// <summary>Every scene is an AI video clip - most expressive, most expensive.</summary>
    AllVideo = 0,

    /// <summary>AI video on the hook + climax, cheap AI stills for the rest.</summary>
    CostOptimized = 1,

    /// <summary>Every scene is a cheap AI still with a Ken-Burns move. Lowest cost.</summary>
    AllImages = 2
}

/// <param name="Strategy">
/// Optional override. When null the project's <see cref="CreditStrategy"/>
/// (Step 2) decides: Balanced -> CostOptimized, MaxImpact -> AllVideo,
/// Economy -> AllImages.
/// </param>
public record GenerateClipPlanRequest(int ClipCount, int ClipDurationSeconds, ClipPlanStrategy? Strategy = null);

public interface IClipPlanService
{
    /// <summary>
    /// Replaces the storyboard's scenes with ClipCount new ones, splitting the
    /// script narration across them by sentence, then runs the
    /// <see cref="IVideoAllocationPlanner"/> to decide - per scene, not by fixed
    /// timestamp - which are AI video (and at which tier) and which are stills,
    /// recording the priority and the reason on each scene.
    /// </summary>
    Task<StoryboardResponse> GenerateAsync(Guid contentProjectId, GenerateClipPlanRequest request, CancellationToken cancellationToken = default);
}

public class ClipPlanService : IClipPlanService
{
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private readonly IScriptService _scriptService;
    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IContentProjectRepository _projectRepository;
    private readonly IVideoAllocationPlanner _allocationPlanner;
    private readonly IStoryVisualContextResolver _storyVisualContextResolver;
    private readonly CreditCostOptions _creditCosts;

    public ClipPlanService(
        IScriptService scriptService,
        IStoryboardRepository storyboardRepository,
        IContentProjectRepository projectRepository,
        IVideoAllocationPlanner allocationPlanner,
        IStoryVisualContextResolver storyVisualContextResolver,
        IOptions<CreditCostOptions> creditCosts)
    {
        _scriptService = scriptService;
        _storyboardRepository = storyboardRepository;
        _projectRepository = projectRepository;
        _allocationPlanner = allocationPlanner;
        _storyVisualContextResolver = storyVisualContextResolver;
        _creditCosts = creditCosts.Value;
    }

    public async Task<StoryboardResponse> GenerateAsync(Guid contentProjectId, GenerateClipPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ClipCount is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "ClipCount must be between 1 and 30.");
        }

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var script = await _scriptService.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException("No script found - run \"Generate with AI\" first.");

        var fullNarration = string.Join(" ", new[] { script.Hook, script.Introduction, script.Body, script.Escalation, script.Payoff, script.CallToAction }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var chunks = SplitIntoChunks(fullNarration, request.ClipCount);

        var strategy = request.Strategy ?? MapCreditStrategy(project.IdeaConfig.CreditStrategy);
        var allocation = AllocateScenes(chunks.Count, strategy);

        var existing = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken);

        Storyboard storyboard;
        if (existing is null)
        {
            storyboard = Storyboard.Create(contentProjectId);
            await _storyboardRepository.AddAsync(storyboard, cancellationToken);
        }
        else
        {
            await _storyboardRepository.DeleteScenesByStoryboardIdAsync(existing.Id, cancellationToken);
            storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
                ?? throw new InvalidOperationException("Storyboard not found after clearing its scenes.");
        }

        // Deterministic, no-AI name matching against the Story's cast/location
        // names - null for every non-Story project (the normal/default case),
        // which skips tagging entirely and leaves every scene exactly as it
        // was before this feature existed.
        var storyReferenceNames = await _storyVisualContextResolver.GetStoryCastAndLocationNamesAsync(contentProjectId, cancellationToken);

        for (var i = 0; i < chunks.Count; i++)
        {
            var slot = allocation[i];
            var scene = storyboard.AddScene(
                request.ClipDurationSeconds,
                chunks[i],
                string.Empty,
                string.Empty,
                slot.AssetType);

            scene.SetAllocation(
                aiVideoPriority: PriorityFor(i, chunks.Count),
                modelTier: slot.ModelTier?.ToString(),
                rationale: slot.Rationale);

            if (storyReferenceNames is { Count: > 0 })
            {
                var matchedNames = MatchReferenceNames(scene.Narration, scene.VisualDescription, storyReferenceNames);
                if (matchedNames.Count > 0)
                {
                    scene.SetRelevantReferenceLabels(matchedNames);
                }
            }
        }

        await _storyboardRepository.SaveChangesAsync(cancellationToken);

        var responseStoryboard = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException("Storyboard not found after generation");
        return StoryboardResponse.FromDomain(responseStoryboard);
    }

    private IReadOnlyList<SceneAllocation> AllocateScenes(int sceneCount, ClipPlanStrategy strategy)
    {
        var candidates = Enumerable.Range(0, sceneCount)
            .Select(i => new AllocationCandidate(
                SceneNumber: i + 1,
                DurationSeconds: 0,
                AiVideoPriority: ShapePriority(PriorityFor(i, sceneCount), i, sceneCount, strategy),
                ImagePriority: 60))
            .ToList();

        // The allocation is budgeted against the daily pool; the live remaining
        // balance is re-checked at generation time (SceneAssetGenerator / the
        // credit ledger), and warned about in the UI before the user starts.
        return _allocationPlanner.Allocate(candidates, _creditCosts.DailyBudgetCredits, _creditCosts).Scenes;
    }

    /// <summary>Base priority by position: hook highest, climax high, CTA low.</summary>
    private static int PriorityFor(int index, int total)
    {
        if (total <= 1) return 100;
        if (index == 0) return 100;                 // hook / hero
        if (index == total - 1) return 20;          // call to action
        if (index == total / 2) return 85;          // climax
        return 55;                                  // supporting beat
    }

    /// <summary>
    /// Bends the position priority to the chosen strategy without touching the
    /// allocator: MaxImpact/AllVideo lifts everything over the Lite floor;
    /// Economy/AllImages zeros everything except the single hero.
    /// </summary>
    private static int ShapePriority(int basePriority, int index, int total, ClipPlanStrategy strategy) => strategy switch
    {
        ClipPlanStrategy.AllVideo => Math.Max(basePriority, VideoAllocationPlanner.LitePriorityFloor + 5),
        ClipPlanStrategy.AllImages => index == 0 ? basePriority : 0,
        _ => basePriority // CostOptimized: leave the position priorities as they are
    };

    private static ClipPlanStrategy MapCreditStrategy(CreditStrategy strategy) => strategy switch
    {
        CreditStrategy.MaxImpact => ClipPlanStrategy.AllVideo,
        CreditStrategy.Economy => ClipPlanStrategy.AllImages,
        _ => ClipPlanStrategy.CostOptimized
    };

    /// <summary>
    /// Deterministic, no-AI name matching: scans a scene's narration +
    /// visual description for a case-insensitive whole-word/whole-phrase
    /// match of each candidate Story character/location name. Word-boundary
    /// aware so a short name (e.g. a location literally named "An", as in
    /// "Hoi An") can't false-match inside an unrelated word like "Ancient".
    /// Multi-word names (e.g. "Ha Long Bay") are matched as the exact phrase
    /// with boundaries at both ends, not word-by-word.
    /// </summary>
    private static List<string> MatchReferenceNames(string narration, string visualDescription, IReadOnlyList<string> candidateNames)
    {
        var text = string.Join(" ", new[] { narration, visualDescription }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var matches = new List<string>();
        foreach (var name in candidateNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmed = name.Trim();
            var pattern = $@"\b{Regex.Escape(trimmed)}\b";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                matches.Add(trimmed);
            }
        }

        return matches;
    }

    private static List<string> SplitIntoChunks(string text, int clipCount)
    {
        var sentences = SentenceSplit.Split(text.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        if (sentences.Count == 0)
        {
            return Enumerable.Repeat(string.Empty, clipCount).ToList();
        }

        var totalChars = sentences.Sum(s => s.Length);
        var targetCharsPerChunk = Math.Max(1, totalChars / clipCount);

        var chunks = new List<string>();
        var current = new List<string>();
        var currentChars = 0;

        foreach (var sentence in sentences)
        {
            if (currentChars > 0 && currentChars + sentence.Length > targetCharsPerChunk && chunks.Count < clipCount - 1)
            {
                chunks.Add(string.Join(" ", current));
                current = new List<string>();
                currentChars = 0;
            }

            current.Add(sentence);
            currentChars += sentence.Length;
        }

        if (current.Count > 0)
        {
            chunks.Add(string.Join(" ", current));
        }

        while (chunks.Count < clipCount)
        {
            chunks.Add(string.Empty);
        }

        return chunks;
    }
}
