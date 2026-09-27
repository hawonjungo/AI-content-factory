using AiContentFactory.Application.Agents;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.AssetReferences;

/// <param name="Prompt">The full generation prompt (default or user-edited).</param>
/// <param name="NegativePrompt">Negative prompt actually sent to the provider for this type.</param>
public record SuggestedReferencePrompt(string Prompt, string NegativePrompt);

public interface IAssetReferenceGenerationService
{
    /// <summary>
    /// Produces <paramref name="count"/> candidate images of one type for the
    /// user to choose from (Character is always exactly 1). A non-null
    /// <paramref name="customPrompt"/> is the user-reviewed / edited prompt and
    /// is sent as-is - the default prompt is never silently substituted.
    /// </summary>
    Task GenerateAsync(Guid contentProjectId, AssetReferenceType type, int count, string? customPrompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// The default prompt (and negative prompt) this project would use for a
    /// reference type right now - so the wizard can show it for review/edit
    /// before spending anything.
    /// </summary>
    Task<SuggestedReferencePrompt> BuildSuggestedPromptAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default);
}

public class AssetReferenceGenerationService : IAssetReferenceGenerationService
{
    private const int MaxVariants = 4;

    private readonly IContentProjectRepository _projectRepository;
    private readonly IAssetReferenceRepository _referenceRepository;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly IAssetReferencePromptAgent _promptAgent;
    private readonly IScriptService _scriptService;
    private readonly IFileStorage _fileStorage;
    private readonly IAiUsageTracker _usageTracker;
    private readonly PricingOptions _pricing;
    private readonly ILogger<AssetReferenceGenerationService> _logger;

    public AssetReferenceGenerationService(
        IContentProjectRepository projectRepository,
        IAssetReferenceRepository referenceRepository,
        IImageGenerationProvider imageProvider,
        IAssetReferencePromptAgent promptAgent,
        IScriptService scriptService,
        IFileStorage fileStorage,
        IAiUsageTracker usageTracker,
        IOptions<PricingOptions> pricing,
        ILogger<AssetReferenceGenerationService> logger)
    {
        _projectRepository = projectRepository;
        _referenceRepository = referenceRepository;
        _imageProvider = imageProvider;
        _promptAgent = promptAgent;
        _scriptService = scriptService;
        _fileStorage = fileStorage;
        _usageTracker = usageTracker;
        _pricing = pricing.Value;
        _logger = logger;
    }

    public async Task<SuggestedReferencePrompt> BuildSuggestedPromptAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var style = PresetCatalog.ResolveStyle(project.StylePresetId);
        var script = await _scriptService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        return await ResolveAsync(type, project, BuildStoryContext(script), style, customPrompt: null, cancellationToken);
    }

    public async Task GenerateAsync(Guid contentProjectId, AssetReferenceType type, int count, string? customPrompt, CancellationToken cancellationToken = default)
    {
        // Character reference is a single reusable anchor - exactly one image.
        // Other types keep the small-variant behaviour, still capped.
        count = type == AssetReferenceType.Character ? 1 : Math.Clamp(count, 1, MaxVariants);

        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var style = PresetCatalog.ResolveStyle(project.StylePresetId);
        var script = await _scriptService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        var storyContext = BuildStoryContext(script);

        var resolved = await ResolveAsync(type, project, storyContext, style, customPrompt, cancellationToken);
        var prompt = resolved.Prompt;
        var negativePrompt = resolved.NegativePrompt;

        try
        {
            project.ReportProgress(AssetGenerationService.ReferencesStage, 0, count, TypeLabel(type));
            await _projectRepository.SaveChangesAsync(cancellationToken);

            // Fresh set of variants each time - a superseded/rejected image
            // shouldn't linger in the picker.
            await _referenceRepository.ClearVariantsAsync(contentProjectId, type, cancellationToken);

            var created = new List<AssetReference>();
            for (var i = 0; i < count; i++)
            {
                // Gemini's image model returns one image per call, so "count"
                // is a call loop; there is no provider-side n/count parameter.
                var image = await _imageProvider.GenerateAsync(new ImageGenerationRequest(prompt, negativePrompt), cancellationToken);
                var extension = image.MimeType.Contains("png") ? "png" : "jpg";
                var storedPath = await _fileStorage.SaveAsync(
                    $"content-projects/{contentProjectId}/asset-references/{type.ToString().ToLowerInvariant()}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{i}.{extension}",
                    image.ImageBytes,
                    cancellationToken);

                created.Add(AssetReference.CreateGenerated(contentProjectId, type, storedPath, prompt, image.Model));

                await _usageTracker.RecordAsync(
                    new RecordUsageInput("gemini", image.Model, "reference_image_generation", _pricing.ImageUsd, contentProjectId, null),
                    cancellationToken);

                project.ReportProgress(AssetGenerationService.ReferencesStage, i + 1, count, TypeLabel(type));
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            await _referenceRepository.AddRangeAsync(created, cancellationToken);
            await _referenceRepository.SaveChangesAsync(cancellationToken);

            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Generated {Count} {Type} reference variant(s) for {ProjectId}", created.Count, type, contentProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Asset reference generation failed for {ProjectId} ({Type})", contentProjectId, type);
            var fresh = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (fresh is not null)
            {
                // A reference-image failure is recoverable - don't drag the
                // project into Failed, just stop showing progress.
                fresh.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }
            throw;
        }
    }

    private static string TypeLabel(AssetReferenceType type) =>
        type == AssetReferenceType.Character ? "Đang tạo ảnh mẫu nhân vật" : "Đang tạo ảnh mẫu bối cảnh";

    /// <summary>
    /// Turns a type + project context into the exact prompt and negative prompt
    /// to send. The default prompt is built by <see cref="IAssetReferencePromptAgent"/>
    /// from the project's actual script, style preset and Step 2 story hints -
    /// nothing about subject species or art style is hardcoded here. Only the
    /// style preset's LOOK-ONLY reference fields (<see cref="StylePreset.ReferenceLookGuidance"/>
    /// / <see cref="StylePreset.ReferenceNegativePrompt"/>) are used, never the
    /// scene-oriented guidance, so a style can't fight the plain-background /
    /// empty-plate staging. A
    /// non-empty <paramref name="customPrompt"/> is sent verbatim (plus the
    /// style line) and never routed through the agent - the default is never
    /// silently substituted for what the user reviewed and edited.
    /// </summary>
    private async Task<SuggestedReferencePrompt> ResolveAsync(
        AssetReferenceType type,
        ContentProject project,
        string storyContext,
        StylePreset style,
        string? customPrompt,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            var prompt = $"{customPrompt.Trim()}\n\nVisual style: {style.ReferenceLookGuidance}.";
            // The wording is the user's, but the type-specific exclusions (no
            // scenery for a Character sheet, no people for an Environment plate)
            // are code-owned and always applied on top of the style's own.
            return new SuggestedReferencePrompt(prompt, AssetReferencePromptAgent.MergeWithQualityNegative(type, style.ReferenceNegativePrompt));
        }

        var idea = project.IdeaConfig;
        var output = await _promptAgent.GenerateAsync(
            new AssetReferencePromptAgentInput(
                AssetType: type.ToString(),
                Title: project.Title,
                Topic: project.Topic,
                Niche: project.Niche,
                StoryContext: storyContext,
                StoryType: idea.StoryType,
                HookStyle: idea.HookStyle,
                Emotion: idea.Emotion,
                StyleGuidance: style.ReferenceLookGuidance,
                StyleNegativePrompt: style.ReferenceNegativePrompt,
                AspectRatio: project.AspectRatio),
            cancellationToken);

        return new SuggestedReferencePrompt(output.Prompt, output.NegativePrompt);
    }

    private static string BuildStoryContext(ScriptResponse? script)
    {
        if (script is null) return "No script is available yet.";

        var text = string.Join("\n", new[]
        {
            script.Hook, script.Introduction, script.Body, script.Escalation, script.Payoff
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return text.Length <= 2_500 ? text : text[..2_500];
    }
}
