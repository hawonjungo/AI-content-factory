using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Infrastructure.Persistence;
using AiContentFactory.Infrastructure.Persistence.Repositories;
using AiContentFactory.Infrastructure.Providers.Gemini;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiContentFactory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres configuration value.");

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IContentProjectRepository, ContentProjectRepository>();
        services.AddScoped<IScriptRepository, ScriptRepository>();
        services.AddScoped<IStoryboardRepository, StoryboardRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();

        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        services.AddHttpClient<ILlmProvider, GeminiLlmProvider>((sp, client) =>
        {
            var options = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer();

        return services;
    }
}
