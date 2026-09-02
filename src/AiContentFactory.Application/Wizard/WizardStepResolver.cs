using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Qa;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.Wizard;

/// <param name="Step">The furthest step the project's data supports - where the wizard should open.</param>
/// <param name="ReachableSteps">Every step the user may navigate to (all steps up to and including Step).</param>
public record WizardState(WizardStep Step, IReadOnlyList<WizardStep> ReachableSteps, IReadOnlyList<string> Blockers);

/// <param name="ReferencesResolved">Both Character and Environment references are Approved or Skipped.</param>
/// <param name="HasFinalVideo">A rendered video exists - not merely that clips do.</param>
public record WizardFacts(
    bool HasTemplate,
    bool HasScript,
    bool ReferencesResolved,
    bool HasClipPlan,
    int TotalClips,
    int ReadyClips,
    bool HasFinalVideo);

/// <summary>
/// Maps pipeline state onto the six wizard steps, and is the ONLY place that
/// knows how ContentProjectStatus, SceneStatus, and AssetStatus relate to what
/// the user sees.
///
/// The rule everywhere downstream: statuses and stage keys never leave this
/// file as-is. The user reads "Đang dựng clip 3/5", not "Generating"; and
/// "Cần tạo kịch bản trước", not "StoryboardReady".
/// </summary>
public static class WizardStepResolver
{
    public static WizardState Resolve(ContentProjectStatus status, WizardFacts facts)
    {
        var blockers = new List<string>();
        var step = DetermineStep(facts);

        if (!facts.HasTemplate)
        {
            blockers.Add("Hãy chọn một mẫu nội dung để bắt đầu.");
        }
        else if (!facts.HasScript)
        {
            blockers.Add("Cần tạo kịch bản trước khi dựng video.");
        }
        else if (!facts.ReferencesResolved)
        {
            blockers.Add("Hãy chốt ảnh mẫu Nhân vật và Bối cảnh (hoặc bấm bỏ qua từng loại).");
        }
        else if (!facts.HasClipPlan)
        {
            blockers.Add("Hãy chọn độ dài video và số clip.");
        }
        else if (facts.ReadyClips < facts.TotalClips)
        {
            blockers.Add(facts.ReadyClips == 0
                ? "Chưa có clip nào được dựng."
                : $"Còn {facts.TotalClips - facts.ReadyClips}/{facts.TotalClips} clip chưa dựng xong.");
        }
        else if (!facts.HasFinalVideo)
        {
            blockers.Add("Hãy ghép các clip thành video hoàn chỉnh.");
        }

        if (status == ContentProjectStatus.Failed)
        {
            blockers.Add("Lần chạy trước bị lỗi. Hãy thử lại bước đang dở.");
        }

        var reachable = Enum.GetValues<WizardStep>().Where(s => s <= step).ToList();
        return new WizardState(step, reachable, blockers);
    }

    private static WizardStep DetermineStep(WizardFacts facts)
    {
        if (!facts.HasTemplate) return WizardStep.Template;
        if (!facts.HasScript) return WizardStep.Idea;
        if (!facts.ReferencesResolved) return WizardStep.References;
        // Clip planning lives inside the Generate step, so "has a script and
        // references but no plan yet" opens there - not back on Script (which
        // would also drop References out of ReachableSteps).
        if (!facts.HasClipPlan) return WizardStep.Generate;
        if (facts.TotalClips == 0 || facts.ReadyClips < facts.TotalClips) return WizardStep.Generate;
        if (!facts.HasFinalVideo) return WizardStep.Preview;
        return WizardStep.Export;
    }

    /// <summary>
    /// Turns a GenerationProgress into something worth reading. Stage keys are
    /// internal; the label is not.
    /// </summary>
    public static WizardProgressDto Describe(GenerationProgress progress)
    {
        if (progress.IsIdle)
        {
            return new WizardProgressDto(false, GenerationProgress.IdleStage, string.Empty, 0, 0, 0);
        }

        var label = progress.Message ?? progress.Stage switch
        {
            ContentPipelineService.ScriptStage => "Đang viết kịch bản",
            QaService.QaStage => "Đang chấm điểm kịch bản",
            AssetGenerationService.ReferencesStage => "Đang tạo ảnh mẫu",
            AssetGenerationService.ClipsStage => "Đang dựng clip",
            GoogleFlowAssetGenerationService.Stage => "Đang dựng video (Google Flow)",
            ClipRegenerationService.ClipStage => "Đang tạo lại clip",
            RenderService.RenderStage => "Đang ghép video",
            _ => "Đang xử lý"
        };

        return new WizardProgressDto(
            true,
            progress.Stage,
            label,
            progress.PercentComplete,
            progress.CompletedUnits,
            progress.TotalUnits);
    }

    /// <summary>
    /// A 0-10 QA number means little on its own; this is the verdict the user
    /// can act on. The threshold mirrors QaOptions.MinimumOverallScoreToProceed
    /// (default 6.0), which is what actually gates the pipeline.
    /// </summary>
    public static string DescribeQaScore(double overall, double passThreshold) => overall switch
    {
        var s when s >= 8.5 => "Rất tốt",
        var s when s >= 7 => "Tốt",
        var s when s >= passThreshold => "Tạm ổn",
        _ => "Nên viết lại"
    };
}
