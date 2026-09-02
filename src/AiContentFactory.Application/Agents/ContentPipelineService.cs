using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Agents;

public interface IContentPipelineService
{
    /// <summary>
    /// Runs the Script Agent and persists the result. Storyboard/clip
    /// planning is now a separate user-driven step (ClipPlanService) rather
    /// than AI-decided upfront, and per-clip generation prompts are created
    /// lazily during asset generation - so this is just script generation,
    /// despite the "pipeline" name kept for API/job-class stability.
    /// </summary>
    Task RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class ContentPipelineService : IContentPipelineService
{
    public const string ScriptStage = "script";

    private readonly IContentProjectRepository _projectRepository;
    private readonly IScriptService _scriptService;
    private readonly IScriptAgent _scriptAgent;
    private readonly ILogger<ContentPipelineService> _logger;

    public ContentPipelineService(
        IContentProjectRepository projectRepository,
        IScriptService scriptService,
        IScriptAgent scriptAgent,
        ILogger<ContentPipelineService> logger)
    {
        _projectRepository = projectRepository;
        _scriptService = scriptService;
        _scriptAgent = scriptAgent;
        _logger = logger;
    }

    public async Task RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        try
        {
            _logger.LogInformation("Script generation started for ContentProject {ContentProjectId}", contentProjectId);

            // Script generation leaves the project in Draft until it succeeds,
            // so status alone can't tell the wizard that a job is running.
            // Progress is the signal it polls.
            project.ReportProgress(ScriptStage, 0, 1, "Đang viết kịch bản");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            var template = PresetCatalog.ResolveTemplate(project.TemplateId);

            var idea = project.IdeaConfig;
            var scriptOutput = await _scriptAgent.GenerateAsync(
                new ScriptAgentInput(
                    project.Title,
                    project.Topic,
                    project.Niche ?? template.Niche,
                    project.TargetDurationSeconds,
                    template.ScriptGuidance,
                    project.Language,
                    idea.ContentPillar,
                    idea.TargetAudience,
                    idea.StoryType,
                    idea.HookStyle,
                    idea.Emotion),
                cancellationToken);

            await _scriptService.UpsertAsync(
                contentProjectId,
                new UpsertScriptRequest(scriptOutput.Hook, scriptOutput.Introduction, scriptOutput.Body, scriptOutput.Escalation, scriptOutput.Payoff, scriptOutput.CallToAction),
                cancellationToken);

            project.TransitionToIfNeeded(ContentProjectStatus.ScriptReady);
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Script generated for ContentProject {ContentProjectId}", contentProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Script generation failed for ContentProject {ContentProjectId}", contentProjectId);

            var freshProject = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken);
            if (freshProject is not null)
            {
                freshProject.TransitionToIfNeeded(ContentProjectStatus.Failed);
                freshProject.ClearProgress();
                await _projectRepository.SaveChangesAsync(cancellationToken);
            }

            throw;
        }
    }
}
