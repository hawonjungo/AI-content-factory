using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
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

        services.AddScoped<IScriptAgent, ScriptAgent>();
        services.AddScoped<IStoryboardAgent, StoryboardAgent>();
        services.AddScoped<IPromptAgent, PromptAgent>();
        services.AddScoped<IContentPipelineService, ContentPipelineService>();

        return services;
    }
}
