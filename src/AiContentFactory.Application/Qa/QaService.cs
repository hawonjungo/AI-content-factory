using AiContentFactory.Application.Agents;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Qa;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Qa;

public interface IQaService
{
    Task<QaScoreResponse> RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QaScoreResponse>> GetHistoryAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs right after script generation (ScriptReady), before any Veo spend -
/// see QaAgent for why. On pass, moves the project to StoryboardReady (user
/// can now set up the clip plan); on fail, sends it back to ScriptReady so
/// the script can be regenerated/edited instead of wasting money on clips
/// from a weak script.
/// </summary>
public class QaService : IQaService
{
    public const string QaStage = "qa";

    private readonly IContentProjectRepository _projectRepository;
    private readonly IScriptService _scriptService;
    private readonly IQaAgent _qaAgent;
    private readonly IQaScoreRepository _qaScoreRepository;
    private readonly QaOptions _qaOptions;
    private readonly ILogger<QaService> _logger;

    public QaService(
        IContentProjectRepository projectRepository,
        IScriptService scriptService,
        IQaAgent qaAgent,
        IQaScoreRepository qaScoreRepository,
        IOptions<QaOptions> qaOptions,
        ILogger<QaService> logger)
    {
        _projectRepository = projectRepository;
        _scriptService = scriptService;
        _qaAgent = qaAgent;
        _qaScoreRepository = qaScoreRepository;
        _qaOptions = qaOptions.Value;
        _logger = logger;
    }

    public async Task<QaScoreResponse> RunAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"ContentProject '{contentProjectId}' was not found.");

        var script = await _scriptService.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new InvalidOperationException("No script found - run \"Generate with AI\" first.");

        try
        {
            project.ReportProgress(QaStage, 0, 1, "Đang chấm điểm kịch bản");
            await _projectRepository.SaveChangesAsync(cancellationToken);

            var scriptOutput = new ScriptAgentOutput(script.Hook, script.Introduction, script.Body, script.Escalation, script.Payoff, script.CallToAction);
            var output = await _qaAgent.ScoreAsync(new QaAgentInput(project.Title, scriptOutput), cancellationToken);

            // VisualQuality/AudioQuality/SubtitleQuality/Consistency columns predate
            // this pre-flight repositioning (they scored a finished storyboard).
            // Kept in the schema to avoid another migration; not meaningful here,
            // so mirrored to Overall rather than left at a misleading 0.
            var score = QaScore.Create(
                contentProjectId, output.Hook, output.Story, output.Pacing,
                visualQuality: output.Overall, audioQuality: output.Overall, subtitleQuality: output.Overall, consistency: output.Overall,
                output.FactualAccuracy, output.PlatformSuitability, output.Overall, output.Notes);

            await _qaScoreRepository.AddAsync(score, cancellationToken);
            await _qaScoreRepository.SaveChangesAsync(cancellationToken);

            project.TransitionToIfNeeded(output.Overall >= _qaOptions.MinimumOverallScoreToProceed
                ? ContentProjectStatus.StoryboardReady
                : ContentProjectStatus.ScriptReady);
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "QA complete for ContentProject {ContentProjectId}: overall {Overall} (threshold {Threshold}) -> {Status}",
                contentProjectId, output.Overall, _qaOptions.MinimumOverallScoreToProceed, project.Status);

            return QaScoreResponse.FromDomain(score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QA failed for ContentProject {ContentProjectId}", contentProjectId);

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

    public async Task<IReadOnlyList<QaScoreResponse>> GetHistoryAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var scores = await _qaScoreRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        return scores.OrderByDescending(s => s.CreatedAt).Select(QaScoreResponse.FromDomain).ToList();
    }
}
