using AiContentFactory.Application;
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
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Infrastructure.Persistence;
using AiContentFactory.Infrastructure.Persistence.Repositories;
using AiContentFactory.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace AiContentFactory.IntegrationTests;

/// <summary>
/// A real PostgreSQL (Testcontainers) + the real EF Core DbContext / repositories
/// / Application services, with the AI providers faked. Proves the new Flow
/// pieces (schema migration, FlowClipImportService, FlowGenerationPlanService,
/// GenerationAttempt ledger) against a real database, without any real Flow
/// credits. Skips cleanly when Docker is unavailable.
/// </summary>
public sealed class PostgresHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    private ServiceProvider? _provider;
    private string _storageRoot = string.Empty;

    public bool Available { get; private set; }
    public string? SkipReason { get; private set; }

    public FakeMediaProbe Probe { get; } = new();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Docker/Testcontainers unavailable: {ex.Message}";
            return;
        }

        _storageRoot = Directory.CreateTempSubdirectory("acf-int-").FullName;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_container.GetConnectionString()));

        services.AddScoped<IContentProjectRepository, ContentProjectRepository>();
        services.AddScoped<IScriptRepository, ScriptRepository>();
        services.AddScoped<IStoryboardRepository, StoryboardRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IAssetReferenceRepository, AssetReferenceRepository>();
        services.AddScoped<IAiUsageRepository, AiUsageRepository>();
        services.AddScoped<IGenerationAttemptRepository, GenerationAttemptRepository>();
        services.AddScoped<IQaScoreRepository, QaScoreRepository>();

        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IMediaProbe>(Probe);

        // Options - defaults are enough for these tests.
        services.Configure<AiContentFactory.Infrastructure.Providers.Gemini.GeminiOptions>(_ => { });
        services.Configure<LocalFileStorageOptions>(o => o.RootPath = _storageRoot);
        services.Configure<BudgetOptions>(_ => { });
        services.Configure<PricingOptions>(_ => { });
        services.Configure<QaOptions>(_ => { });
        services.Configure<VideoGenerationOptions>(_ => { });
        services.Configure<GoogleFlowOptions>(_ => { });
        services.Configure<CreditCostOptions>(_ => { });
        services.Configure<FlowModelOptions>(_ => { });
        services.Configure<CaptionSegmentationOptions>(_ => { });
        services.Configure<TimelineOptions>(_ => { });
        services.Configure<AudioMixOptions>(_ => { });

        services.AddApplication();

        // Fake AI providers - no real LLM / image / Flow calls.
        services.AddScoped<ILlmProvider, FakeLlmProvider>();
        services.AddScoped<IImageGenerationProvider, FakeImageProvider>();
        services.AddScoped<IVideoGenerationProvider, FakeVideoProvider>();
        services.AddScoped<ITtsProvider, FakeTtsProvider>();

        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        Available = true;
    }

    public AsyncServiceScope Scope() =>
        (_provider ?? throw new InvalidOperationException("host not initialized")).CreateAsyncScope();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        try
        {
            if (!string.IsNullOrEmpty(_storageRoot) && Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch { /* ignore */ }

        await _container.DisposeAsync();
    }
}

/// <summary>Settable media probe so a test can drive import validation without ffmpeg.</summary>
public sealed class FakeMediaProbe : IMediaProbe
{
    public MediaInfo Next { get; set; } =
        new(true, null, 4.0, 1080, 1920, 30, HasVideo: true, HasAudio: false, AudioDurationSeconds: 0);

    public Task<MediaInfo> ProbeAsync(string absolutePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Next);
}
