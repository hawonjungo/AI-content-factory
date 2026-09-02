using System.Text;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <param name="RecommendedModel">"Fast" / "Lite" for an AI_VIDEO scene; null otherwise.</param>
/// <param name="FlowVideoPrompt">Ready to paste into Google Flow's prompt box.</param>
/// <param name="ReferenceImageUrls">API-relative URLs of the approved Character/Environment anchors to attach in Flow.</param>
public record FlowPlanScene(
    Guid SceneId,
    int SceneNumber,
    string GenerationType,
    string? RecommendedModel,
    int EstimatedCredits,
    int Priority,
    string NarrationText,
    string CaptionText,
    string VisualDescription,
    string FlowVideoPrompt,
    string ImagePrompt,
    bool CharacterRequired,
    IReadOnlyList<string> ReferenceImageUrls,
    string? Rationale,
    bool SkipGeneration = false);

/// <summary>
/// Everything the user needs to run the video-generation step in Google Flow
/// efficiently. The app never calls Flow - it prepares prompts, reference
/// assets, a model recommendation per scene, and a credit budget so the user
/// spends their free daily Flow credits deliberately (and never has to spend
/// all of them).
/// </summary>
public record FlowGenerationPlan(
    int DailyBudgetCredits,
    int UsedCredits,
    int RemainingCredits,
    int PlannedCredits,
    int ScenesRequiringFlow,
    bool WithinBudget,
    string FastModelLabel,
    string LiteModelLabel,
    IReadOnlyList<FlowPlanScene> Scenes,
    string CopyAllText);

public interface IFlowGenerationPlanService
{
    Task<FlowGenerationPlan> BuildAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class FlowGenerationPlanService : IFlowGenerationPlanService
{
    private readonly IContentProjectRepository _projectRepository;
    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IAssetReferenceRepository _assetReferenceRepository;
    private readonly ICreditLedger _creditLedger;
    private readonly CreditCostOptions _costs;
    private readonly FlowModelOptions _models;

    public FlowGenerationPlanService(
        IContentProjectRepository projectRepository,
        IStoryboardRepository storyboardRepository,
        IAssetReferenceRepository assetReferenceRepository,
        ICreditLedger creditLedger,
        IOptions<CreditCostOptions> costs,
        IOptions<FlowModelOptions> models)
    {
        _projectRepository = projectRepository;
        _storyboardRepository = storyboardRepository;
        _assetReferenceRepository = assetReferenceRepository;
        _creditLedger = creditLedger;
        _costs = costs.Value;
        _models = models.Value;
    }

    public async Task<FlowGenerationPlan> BuildAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var style = PresetCatalog.ResolveStyle(project.StylePresetId);
        var storyboard = await _storyboardRepository.GetByContentProjectIdAsyncNoTracking(contentProjectId, cancellationToken);
        var references = await _assetReferenceRepository.GetByProjectAsync(contentProjectId, cancellationToken);
        var usage = await _creditLedger.GetDailyUsageAsync(cancellationToken);

        // The approved Character / Environment anchors, kept distinct so a scene
        // can say exactly which references it has (both are optional).
        string? UrlFor(AssetReferenceType type)
        {
            var r = references.FirstOrDefault(x => x.Type == type && x.Status == AssetReferenceStatus.Approved && !string.IsNullOrWhiteSpace(x.ImagePath));
            return r is null ? null : $"/content-projects/{contentProjectId}/asset-references/{r.Id}/file";
        }

        var characterUrl = UrlFor(AssetReferenceType.Character);
        var environmentUrl = UrlFor(AssetReferenceType.Environment);

        var sceneResponses = storyboard is null
            ? new List<SceneResponse>()
            : storyboard.Scenes.OrderBy(s => s.SceneNumber).Select(SceneResponse.FromDomain).ToList();

        var scenes = sceneResponses
            .Select(s => BuildScene(s, style.VisualStyleGuidance, characterUrl, environmentUrl))
            .ToList();

        // A scene the user already has a clip for needs no Flow work and no credits.
        var plannedCredits = scenes.Where(s => s.GenerationType == "AI_VIDEO" && !s.SkipGeneration).Sum(s => s.EstimatedCredits);
        var scenesRequiringFlow = scenes.Count(s => s.GenerationType == "AI_VIDEO" && !s.SkipGeneration);

        return new FlowGenerationPlan(
            DailyBudgetCredits: _costs.DailyBudgetCredits,
            UsedCredits: usage.Used,
            RemainingCredits: usage.Remaining,
            PlannedCredits: plannedCredits,
            ScenesRequiringFlow: scenesRequiringFlow,
            WithinBudget: plannedCredits <= usage.Remaining,
            FastModelLabel: _models.FastModel,
            LiteModelLabel: _models.LiteModel,
            Scenes: scenes,
            CopyAllText: BuildCopyAll(scenes));
    }

    private FlowPlanScene BuildScene(SceneResponse scene, string styleGuidance, string? characterUrl, string? environmentUrl)
    {
        var generationType = SceneResponse.GenerationTypeOf(ParseVisualType(scene.VisualType));
        var tier = Enum.TryParse<VideoModelTier>(scene.ModelTier, ignoreCase: true, out var t) ? t : (VideoModelTier?)null;

        // Character reference is relevant only when the scene features the
        // protagonist AND an approved Character anchor exists; Environment always
        // helps if one exists. Both are optional.
        var hasCharacter = scene.CharacterRequired && characterUrl is not null;
        var hasEnvironment = environmentUrl is not null;

        var recommendedModel = generationType == "AI_VIDEO" ? (tier ?? VideoModelTier.Lite).ToString() : null;
        var estimatedCredits = generationType switch
        {
            "AI_VIDEO" => _costs.VideoCreditsFor(tier ?? VideoModelTier.Lite),
            "AI_IMAGE" => _costs.ImageCredits,
            _ => 0
        };

        var flowPrompt = ComposeFlowPrompt(scene, styleGuidance, hasCharacter, hasEnvironment);
        var imagePrompt = ComposeImagePrompt(scene, styleGuidance, hasCharacter);

        var referenceUrls = new[] { hasCharacter ? characterUrl : null, hasEnvironment ? environmentUrl : null }
            .Where(u => u is not null)
            .Select(u => u!)
            .ToList();

        return new FlowPlanScene(
            scene.Id,
            scene.SceneNumber,
            generationType,
            recommendedModel,
            estimatedCredits,
            scene.AiVideoPriority,
            scene.Narration,
            scene.EffectiveCaptionText,
            scene.VisualDescription,
            flowPrompt,
            imagePrompt,
            scene.CharacterRequired,
            referenceUrls,
            scene.AllocationRationale,
            scene.SkipGeneration);
    }

    /// <summary>
    /// The Flow video prompt is built by the shared deterministic
    /// <see cref="VideoPromptBuilder"/>: the scene's stored action (never the raw
    /// narration), its structured camera, and only the reference lines whose
    /// anchor actually exists.
    /// </summary>
    private static string ComposeFlowPrompt(SceneResponse scene, string styleGuidance, bool hasCharacterReference, bool hasEnvironmentReference)
    {
        var action = !string.IsNullOrWhiteSpace(scene.GenerationPrompt)
            ? scene.GenerationPrompt!
            : !string.IsNullOrWhiteSpace(scene.VisualDescription)
                ? scene.VisualDescription
                : VideoPromptBuilder.DefaultAction;

        var camera = VideoPromptBuilder.ParseCamera(scene.CameraMovement);
        if (camera == CameraMovement.Unspecified)
        {
            camera = VideoPromptBuilder.ParseCamera(scene.CameraDirection);
        }

        return VideoPromptBuilder.Build(new VideoPromptSpec(
            Action: action,
            Camera: camera,
            HasCharacterReference: hasCharacterReference,
            HasEnvironmentReference: hasEnvironmentReference,
            StyleGuidance: styleGuidance,
            DurationSeconds: scene.DurationSeconds,
            AspectRatio: "9:16"));
    }

    private static string ComposeImagePrompt(SceneResponse scene, string styleGuidance, bool hasCharacterReference)
    {
        var subject = !string.IsNullOrWhiteSpace(scene.VisualDescription) ? scene.VisualDescription.Trim() : scene.Narration.Trim();
        var character = hasCharacterReference
            ? " Feature the main character exactly as the attached Character reference (real human, correct anatomy)."
            : string.Empty;
        return $"Photorealistic cinematic vertical 9:16 still: {subject}.{character} Visual style: {styleGuidance}. Deliberate framing, clear focal subject, filmic lighting.";
    }

    /// <summary>
    /// The one-click export for the Google Flow step. English only - it is
    /// generation instructions for a model, not UI copy. One block per scene that
    /// still needs a Flow clip, in a fixed "--- SCENE n (Veo Fast - 20 credits) ---"
    /// header + prompt shape; the user pastes one block per Generate. Scenes the
    /// user already has a clip for are left out entirely.
    /// </summary>
    private static string BuildCopyAll(IReadOnlyList<FlowPlanScene> scenes)
    {
        var video = scenes.Where(s => s.GenerationType == "AI_VIDEO" && !s.SkipGeneration).ToList();
        if (video.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < video.Count; i++)
        {
            var s = video[i];
            var model = string.Equals(s.RecommendedModel, "Fast", StringComparison.OrdinalIgnoreCase) ? "Veo Fast" : "Veo Lite";
            sb.AppendLine($"--- SCENE {s.SceneNumber} ({model} - {s.EstimatedCredits} credits) ---");
            sb.AppendLine(s.FlowVideoPrompt);
            if (i < video.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static Domain.Storyboards.SceneVisualType ParseVisualType(string value) =>
        Enum.TryParse<Domain.Storyboards.SceneVisualType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : Domain.Storyboards.SceneVisualType.AiVideo;
}
