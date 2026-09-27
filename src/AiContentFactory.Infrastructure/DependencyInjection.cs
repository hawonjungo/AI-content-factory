using AiContentFactory.Application.Agents;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Qa;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Stories;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Infrastructure.Jobs;
using AiContentFactory.Infrastructure.Persistence;
using AiContentFactory.Infrastructure.Persistence.Repositories;
using AiContentFactory.Infrastructure.Providers;
using AiContentFactory.Infrastructure.Providers.Gemini;
using AiContentFactory.Infrastructure.Providers.Groq;
using AiContentFactory.Infrastructure.Providers.Publishing;
using AiContentFactory.Infrastructure.Publishing;
using AiContentFactory.Infrastructure.Rendering;
using AiContentFactory.Infrastructure.Security;
using AiContentFactory.Infrastructure.Storage;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration value.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IContentProjectRepository, ContentProjectRepository>();
        services.AddScoped<IStoryRepository, StoryRepository>();
        services.AddScoped<IScriptRepository, ScriptRepository>();
        services.AddScoped<IStoryboardRepository, StoryboardRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetReferenceRepository, AssetReferenceRepository>();
        services.AddScoped<IAiUsageRepository, AiUsageRepository>();
        services.AddScoped<IGenerationAttemptRepository, GenerationAttemptRepository>();
        services.AddScoped<IQaScoreRepository, QaScoreRepository>();
        services.AddScoped<IPublishJobRepository, PublishJobRepository>();
        services.AddScoped<ISocialConnectionRepository, SocialConnectionRepository>();

        // Options bindings
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.Configure<LocalFileStorageOptions>(configuration.GetSection(LocalFileStorageOptions.SectionName));
        services.Configure<BudgetOptions>(configuration.GetSection(BudgetOptions.SectionName));
        services.Configure<PricingOptions>(configuration.GetSection(PricingOptions.SectionName));
        services.Configure<QaOptions>(configuration.GetSection(QaOptions.SectionName));
        services.Configure<VideoGenerationOptions>(configuration.GetSection(VideoGenerationOptions.SectionName));
        services.Configure<GoogleFlowOptions>(configuration.GetSection(GoogleFlowOptions.SectionName));
        services.Configure<CreditCostOptions>(configuration.GetSection(CreditCostOptions.SectionName));
        services.Configure<FlowModelOptions>(configuration.GetSection(FlowModelOptions.SectionName));
        services.Configure<CaptionSegmentationOptions>(configuration.GetSection(CaptionSegmentationOptions.SectionName));
        services.Configure<TimelineOptions>(configuration.GetSection(TimelineOptions.SectionName));
        services.Configure<AudioMixOptions>(configuration.GetSection(AudioMixOptions.SectionName));
        services.Configure<PublishingOptions>(configuration.GetSection(PublishingOptions.SectionName));
        services.Configure<TikTokPublishOptions>(configuration.GetSection(TikTokPublishOptions.SectionName));
        services.Configure<YouTubePublishOptions>(configuration.GetSection(YouTubePublishOptions.SectionName));
        services.Configure<InstagramPublishOptions>(configuration.GetSection(InstagramPublishOptions.SectionName));
        services.Configure<FacebookPublishOptions>(configuration.GetSection(FacebookPublishOptions.SectionName));
        services.Configure<GroqOptions>(configuration.GetSection(GroqOptions.SectionName));
        services.Configure<LlmRoutingOptions>(configuration.GetSection(LlmRoutingOptions.SectionName));

        // Storage
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // AI providers - all Gemini-family, all share the same timeout config
        // pattern (video gets its own longer timeout since Veo is async/slow).
        services.AddHttpClient<ILlmProvider, GeminiLlmProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        // Same concrete class, registered again under its own type so ILlmRouter
        // can depend on a specific provider (via the keyed registrations below)
        // instead of "whichever ILlmProvider DI happens to hand out" - every
        // other agent keeps using the ILlmProvider registration above unchanged.
        services.AddHttpClient<GeminiLlmProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        // Billable multimodal "look at these frames" call for the AI clip check.
        services.AddHttpClient<IVisionReviewProvider, GeminiVisionReviewProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        // Optional low-cost/fast provider for ILlmRouter - inert (IsConfigured
        // false) until Llm:Groq:ApiKey is set, so this is a no-op for anyone who
        // hasn't opted in.
        services.AddHttpClient<GroqLlmProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GroqOptions.SectionName).Get<GroqOptions>() ?? new GroqOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddKeyedScoped<ILlmProvider>(LlmProviderKeys.Gemini, (sp, _) => sp.GetRequiredService<GeminiLlmProvider>());
        services.AddKeyedScoped<ILlmProvider>(LlmProviderKeys.Groq, (sp, _) => sp.GetRequiredService<GroqLlmProvider>());
        services.AddScoped<ILlmRouter, LlmRouter>();
        services.AddHttpClient<AiContentFactory.Application.Providers.IImageGenerationProvider, GeminiImageProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddHttpClient<GeminiTtsProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        // Free self-hosted voices (Kokoro). Inert until Tts:Kokoro:BaseUrl is set.
        services.Configure<Providers.Kokoro.KokoroOptions>(configuration.GetSection(Providers.Kokoro.KokoroOptions.SectionName));
        services.AddHttpClient<Providers.Kokoro.KokoroTtsProvider>((sp, client) =>
        {
            var options = configuration.GetSection(Providers.Kokoro.KokoroOptions.SectionName).Get<Providers.Kokoro.KokoroOptions>() ?? new Providers.Kokoro.KokoroOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddScoped<ITtsProvider, Providers.Kokoro.RoutingTtsProvider>();
        // Free stock footage (Pexels). Inert until Stock:Pexels:ApiKey is set.
        services.Configure<Providers.Pexels.PexelsOptions>(configuration.GetSection(Providers.Pexels.PexelsOptions.SectionName));
        services.AddHttpClient<IStockFootageProvider, Providers.Pexels.PexelsStockFootageProvider>((sp, client) =>
        {
            var options = configuration.GetSection(Providers.Pexels.PexelsOptions.SectionName).Get<Providers.Pexels.PexelsOptions>() ?? new Providers.Pexels.PexelsOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        // Downloads are only allowed to pexels.com; a redirect must never take them elsewhere.
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IVideoGenerationProvider, VeoVideoProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.VideoTimeoutSeconds + 30); // headroom over the internal polling deadline
        });

        // Google Flow providers (alternative Nano Banana image + Veo Image-to-Video)
        services.AddHttpClient<NanoBananaImageProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddHttpClient<VeoImageToVideoProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.VideoTimeoutSeconds + 30);
        });

        // Google Flow's video step wants image-to-video, not the default
        // text-to-video - expose VeoImageToVideoProvider under a key so
        // GoogleFlowAssetGenerationService can [FromKeyedServices] it while
        // everything else keeps getting VeoVideoProvider.
        services.AddKeyedScoped<IVideoGenerationProvider>(
            GoogleFlowKeys.Provider,
            (sp, _) => sp.GetRequiredService<VeoImageToVideoProvider>());

        // Google Flow quota management
        services.AddScoped<IGoogleFlowQuotaManager, GoogleFlowQuotaManager>();

        // Hook-heavy script agent for Google Flow
        services.AddScoped<IHookScriptAgent, HookScriptAgent>();

        // Video rendering + probing
        services.AddScoped<IVideoRenderer, FfmpegVideoRenderer>();
        services.AddScoped<IMediaProbe, FfprobeMediaProbe>();
        services.AddScoped<IVideoFrameExtractor, FfmpegFrameExtractor>();

        // Step 7 - social publishing. Token encryption + the Hangfire seam are
        // Infrastructure concerns; each provider gets its own HttpClient with a
        // long timeout (video uploads are slow).
        services.AddScoped<ITokenProtector, AesTokenProtector>();
        services.AddScoped<IPublishJobScheduler, HangfirePublishJobScheduler>();

        services.AddHttpClient<ISocialPlatformPublisher, TikTokPublisher>(c => c.Timeout = TimeSpan.FromMinutes(20));
        services.AddHttpClient<ISocialPlatformPublisher, YouTubePublisher>(c => c.Timeout = TimeSpan.FromMinutes(20));
        services.AddHttpClient<ISocialPlatformPublisher, InstagramPublisher>(c => c.Timeout = TimeSpan.FromMinutes(20));
        services.AddHttpClient<ISocialPlatformPublisher, FacebookPublisher>(c => c.Timeout = TimeSpan.FromMinutes(20));

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer();

        return services;
    }
}
