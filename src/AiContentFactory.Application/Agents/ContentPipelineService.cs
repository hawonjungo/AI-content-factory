using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Agents;

public interface IContentPipelineService
{
    /// <summary>
    /// Runs Script Agent -> Storyboard Agent -> Prompt Agent (per scene) for a
    /// content project, persisting after each step so a failure partway
    /// through doesn't lose prior work. Intended to be called from a
    /// background job (Hangfire), not directly from an HTTP request.
    /// </summary>
    Task RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class ContentPipelineService : IContentPipelineService
{
    private readonly IContentProjectRepository _projectRepository;
    private readonly IScriptService _scriptService;
    private readonly IStoryboardService _storyboardService;
    private readonly IScriptAgent _scriptAgent;
    private readonly IStoryboardAgent _storyboardAgent;
    private readonly IPromptAgent _promptAgent;
    private readonly ILogger<ContentPipelineService> _logger;

    public ContentPipelineService(
        IContentProjectRepository projectRepository,
        IScriptService scriptService,
        IStoryboardService storyboardService,
        IScriptAgent scriptAgent,
        IStoryboardAgent storyboardAgent,
        IPromptAgent promptAgent,
        ILogger<ContentPipelineService> logger)
    {
        _projectRepository = projectRepository;
        _scriptService = scriptService;
        _storyboardService = storyboardService;
        _scriptAgent = scriptAgent;
        _storyboardAgent = storyboardAgent;
        _promptAgent = promptAgent;
        _logger = logger;
    }

    public async Task RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        try
        {
            _logger.LogInformation("Pipeline started for ContentProject {ContentProjectId}", contentProjectId);

            var scriptOutput = await _scriptAgent.GenerateAsync(
                new ScriptAgentInput(project.Title, project.Topic, project.Niche, project.TargetDurationSeconds),
                cancellationToken);

            await _scriptService.UpsertAsync(
                contentProjectId,
                new UpsertScriptRequest(scriptOutput.Hook, scriptOutput.Introduction, scriptOutput.Body, scriptOutput.Escalation, scriptOutput.Payoff, scriptOutput.CallToAction),
                cancellationToken);

            project.TransitionTo(ContentProjectStatus.ScriptReady);
            await _projectRepository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Script generated for ContentProject {ContentProjectId}", contentProjectId);

            var storyboardOutput = await _storyboardAgent.GenerateAsync(
                new StoryboardAgentInput(project.Title, scriptOutput, project.TargetDurationSeconds),
                cancellationToken);

            var storyboard = await _storyboardService.GetOrCreateAsync(contentProjectId, cancellationToken);
            foreach (var scene in storyboardOutput.Scenes.OrderBy(s => s.SceneNumber))
            {
                storyboard = await _storyboardService.AddSceneAsync(
                    contentProjectId,
                    new CreateSceneRequest(scene.DurationSeconds, scene.Narration, scene.VisualDescription, scene.CameraDirection, scene.VisualType),
                    cancellationToken);
            }

            project.TransitionTo(ContentProjectStatus.StoryboardReady);
            await _projectRepository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Storyboard generated for ContentProject {ContentProjectId} with {SceneCount} scenes", contentProjectId, storyboard.Scenes.Count);

            foreach (var scene in storyboard.Scenes)
            {
                var promptOutput = await _promptAgent.GenerateAsync(
                    new PromptAgentInput(scene.Narration, scene.VisualDescription, scene.CameraDirection, Enum.Parse<Domain.Storyboards.SceneVisualType>(scene.VisualType), null),
                    cancellationToken);

                await _storyboardService.SetScenePromptAsync(
                    contentProjectId,
                    scene.Id,
                    promptOutput.Prompt,
                    promptOutput.NegativePrompt,
                    promptOutput.VisualStyle,
                    provider: "gemini",
                    cancellationToken);
            }

            _logger.LogInformation("Pipeline complete for ContentProject {ContentProjectId}", contentProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed for ContentProject {ContentProjectId}", contentProjectId);

            // Reload - the failed step may have left the project in a status
            // whose transition graph doesn't include Failed from every state,
            // but Failed is reachable from every non-terminal status we use here.
            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null && freshProject.Status != ContentProjectStatus.Failed)
            {
                freshProject.TransitionTo(ContentProjectStatus.Failed);
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }
}
