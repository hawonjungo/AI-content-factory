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
using AiContentFactory.Application.Stories;
using AiContentFactory.Application.Stories.Continuity;
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

        // Shared "reserve busy lock, then enqueue" helper used by every
        // controller action that starts a long-running background job
        // (ContentProjectsController's script/assets/prompts/render/QA
        // actions, StoryboardsController's clip-plan auto-chain).
        services.AddScoped<IProjectJobReservationService, ProjectJobReservationService>();

        services.AddScoped<IStoryService, StoryService>();

        // Story -> ContentProject wiring ("Create Video"/"Open Video" on an
        // episode) - pure orchestration over the existing pipeline, no new
        // generation logic.
        services.AddScoped<IStoryVideoLinkService, StoryVideoLinkService>();

        // Story-level Character/Location reference images - generated once,
        // reused across every episode via IStoryVideoLinkService's seeding
        // step. Deterministic prompt composition only, no LLM agent.
        services.AddScoped<IStoryAssetReferenceService, StoryAssetReferenceService>();

        // In-flight guard (one billable generation/upload per character at a time). Process-local, so a singleton.
        services.AddSingleton<IStoryReferenceGenerationGate, StoryReferenceGenerationGate>();

        // Resolves the short Story-continuity blurb fed into a scene's
        // PromptAgentInput.StoryVisualContext when its ContentProject is
        // Story-linked; null (no-op) for every normal, non-Story project.
        services.AddScoped<IStoryVisualContextResolver, StoryVisualContextResolver>();

        // Story/Series AI orchestration (Bible -> Episode Outline -> Script ->
        // Validate -> Finalize) - additive, parallel to the plain-CRUD
        // IStoryService above. Reuses LlmTaskType.Script (see agent files for
        // rationale) rather than adding new routing configuration.
        services.AddScoped<IStoryPlannerAgent, StoryPlannerAgent>();
        services.AddScoped<IEpisodePlannerAgent, EpisodePlannerAgent>();
        services.AddScoped<IStoryScriptWriterAgent, StoryScriptWriterAgent>();
        services.AddScoped<IStoryContinuityAnalysisAgent, StoryContinuityAnalysisAgent>();
        services.AddScoped<IContinuityValidatorAgent, ContinuityValidatorAgent>();
        services.AddScoped<IStoryContinuityManager, StoryContinuityManager>();
        services.AddScoped<IScriptService, ScriptService>();
        services.AddScoped<IStoryboardService, StoryboardService>();
        services.AddScoped<IAssetService, AssetService>();
        services.AddScoped<IClipPlanService, ClipPlanService>();

        // Script generation (Phase 3, now script-only)
        services.AddScoped<IScriptAgent, ScriptAgent>();
        services.AddScoped<IPromptAgent, PromptAgent>();
        services.AddScoped<IContentPipelineService, ContentPipelineService>();

        // Step 2 - AI content idea suggestions (idea-level only, reuses the cheap
        // text ILlmProvider; no scripts/storyboards are produced here).
        services.AddScoped<IContentIdeasAgent, ContentIdeasAgent>();
        services.AddScoped<Ideas.IContentIdeasService, Ideas.ContentIdeasService>();

        // Asset references (Character/Environment consistency anchors the user
        // reviews and approves before any clip is generated)
        services.AddScoped<IAssetReferenceService, AssetReferenceService>();
        services.AddScoped<IAssetReferencePromptAgent, AssetReferencePromptAgent>();
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
        services.AddScoped<ISceneKeyframeService, SceneKeyframeService>();
        services.AddScoped<IFlowKeyframeService, FlowKeyframeService>();
        services.AddScoped<IClipCheckService, ClipCheckService>();
        services.AddScoped<IStockFootageService, StockFootageService>();
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
