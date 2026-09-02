using AiContentFactory.Application.Agents;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Audio;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Qa;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Tts;
using AiContentFactory.Application.Wizard;
using Microsoft.Extensions.DependencyInjection;

namespace AiContentFactory.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IContentProjectService, ContentProjectService>();
        services.AddScoped<IScriptService, ScriptService>();
        services.AddScoped<IStoryboardService, StoryboardService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IClipPlanService, ClipPlanService>();

        // Script generation (Phase 3, now script-only)
        services.AddScoped<IScriptAgent, ScriptAgent>();
        services.AddScoped<IPromptAgent, PromptAgent>();
        services.AddScoped<IContentPipelineService, ContentPipelineService>();

        // Asset references (Character/Environment consistency anchors the user
        // reviews and approves before any clip is generated)
        services.AddScoped<IAssetReferenceService, AssetReferenceService>();
        services.AddScoped<IAssetReferenceGenerationService, AssetReferenceGenerationService>();

        // Preset catalog (templates / style / voice / captions)
        services.AddScoped<IPresetService, PresetService>();

        // Asset generation + cost tracking (Veo-only now). SceneAssetGenerator
        // is the shared per-scene path used by both the full run and the
        // single-clip rerun.
        services.AddScoped<IAiUsageTracker, AiUsageTrackerService>();
        services.AddScoped<IGenerationEstimator, GenerationEstimator>();

        // Core generation pipeline: centralized credit budget, dynamic
        // Fast/Lite allocation, storyboard planning, and the isolated
        // per-modality generation services.
        services.AddScoped<ICreditLedger, CreditLedgerService>();
        services.AddSingleton<IVideoAllocationPlanner, VideoAllocationPlanner>();
        services.AddScoped<IStoryboardPlanner, StoryboardPlanner>();
        services.AddScoped<IFlowGenerationService, FlowGenerationService>();
        services.AddScoped<IImageGenerationService, ImageGenerationService>();

        // Google Flow (human-in-the-loop video step): plan the prompts/assets/
        // credits, then import + validate the clips the user generated in Flow.
        services.AddScoped<IFlowGenerationPlanService, FlowGenerationPlanService>();
        services.AddScoped<IFlowClipImportService, FlowClipImportService>();

        // TTS: narration synthesis + mandatory audio validation, provider stays replaceable.
        services.AddSingleton<IAudioValidator, AudioValidator>();
        services.AddScoped<ITtsService, TtsService>();

        services.AddScoped<ISceneAssetGenerator, SceneAssetGenerator>();
        services.AddScoped<IAssetGenerationService, AssetGenerationService>();
        services.AddScoped<IClipRegenerationService, ClipRegenerationService>();
        services.AddScoped<IGoogleFlowAssetGenerationService, GoogleFlowAssetGenerationService>();
        services.AddScoped<IAssetGenerationDispatcher, AssetGenerationDispatcher>();

        // Wizard projection (the only place pipeline state is translated for users)
        services.AddScoped<IProjectOverviewService, ProjectOverviewService>();

        // Audio-first composition pipeline: real narration timing -> semantic
        // caption segmentation -> narration-driven timeline -> composition ->
        // final quality validation.
        services.AddSingleton<IAudioTimingService, AudioTimingService>();
        services.AddSingleton<ICaptionSegmentationService, CaptionSegmentationService>();
        services.AddSingleton<IAudioMixingService, AudioMixingService>();
        services.AddScoped<ITimelineService, TimelineService>();
        services.AddScoped<IVideoQualityValidator, VideoQualityValidator>();
        services.AddScoped<IVideoCompositionService, VideoCompositionService>();

        // Video rendering
        services.AddScoped<IRenderService, RenderService>();

        // QA (pre-flight script gate) + approval (reuses ContentProjectService.ChangeStatusAsync)
        services.AddScoped<IQaAgent, QaAgent>();
        services.AddScoped<IQaService, QaService>();

        // Step 7 - social publishing / scheduling. Provider APIs, the token
        // protector and the job scheduler are wired in Infrastructure.
        services.AddScoped<Publishing.ISocialConnectionService, Publishing.SocialConnectionService>();
        services.AddScoped<Publishing.IPublishingService, Publishing.PublishingService>();

        return services;
    }
}
