using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="StoryboardService.SuggestAllScenePromptsAsync"/> - the
/// bulk "suggest prompts for every unprompted scene" action - in particular
/// its cost-safety guarantees: it must never re-bill an already-prompted
/// scene, and one scene's PromptAgent failure must not lose the prompts
/// already generated for the rest of the batch.
/// </summary>
public class StoryboardServiceBulkPromptTests
{
    private static (StoryboardService Service, FakeStoryboardRepository StoryboardRepo, FakePromptAgent PromptAgent, FakeContentProjectRepository ProjectRepo)
        Build(ContentProject project)
    {
        var storyboardRepo = new FakeStoryboardRepository(Storyboard.Create(project.Id));
        var promptAgent = new FakePromptAgent();
        var projectRepo = new FakeContentProjectRepository(project);
        var storyRepo = new FakeStoryRepository();
        var resolver = new StoryVisualContextResolver(storyRepo);
        var service = new StoryboardService(storyboardRepo, promptAgent, projectRepo, resolver, NullLogger<StoryboardService>.Instance);
        return (service, storyboardRepo, promptAgent, projectRepo);
    }

    [Fact]
    public async Task Only_calls_the_prompt_agent_for_scenes_matching_the_unprompted_condition()
    {
        var project = ContentProject.Create("Bulk Prompt Test", "topic", "niche", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, _) = Build(project);
        var storyboard = storyboardRepo.Current!;

        // Unprompted: no GenerationPrompt AND no VisualDescription.
        var unprompted1 = storyboard.AddScene(8, "narration 1", "", "push", SceneVisualType.AiVideo);
        var unprompted2 = storyboard.AddScene(8, "narration 2", "", "push", SceneVisualType.AiVideo);

        // Already has a GenerationPrompt - must be left untouched, no re-bill.
        var alreadyPrompted = storyboard.AddScene(8, "narration 3", "", "push", SceneVisualType.AiVideo);
        alreadyPrompted.SetGenerationPrompt("existing action", null, null, "gemini");

        // Has a hand-written VisualDescription (no GenerationPrompt yet) - per
        // FlowGenerationPlanService.IsUnprompted this also counts as "not
        // unprompted", so it must be left alone too.
        var handWrittenVisual = storyboard.AddScene(8, "narration 4", "hand-written visual description", "push", SceneVisualType.AiVideo);

        var result = await service.SuggestAllScenePromptsAsync(project.Id);

        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, result.AlreadyPrompted);
        Assert.Empty(result.Errors);
        Assert.Equal(2, promptAgent.Calls);

        Assert.False(string.IsNullOrWhiteSpace(unprompted1.GenerationPrompt));
        Assert.False(string.IsNullOrWhiteSpace(unprompted2.GenerationPrompt));
        Assert.Equal("existing action", alreadyPrompted.GenerationPrompt);
        Assert.Null(handWrittenVisual.GenerationPrompt);

        // The busy lock ReserveJobAsync would have set before enqueueing must
        // be released once the batch finishes.
        Assert.True(project.Progress.IsIdle);
    }

    [Fact]
    public async Task A_single_scene_failure_does_not_abort_the_batch()
    {
        var project = ContentProject.Create("Bulk Prompt Failure Test", "topic", "niche", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, _) = Build(project);
        var storyboard = storyboardRepo.Current!;

        var sceneA = storyboard.AddScene(8, "scene A narration", "", "push", SceneVisualType.AiVideo);
        var sceneB = storyboard.AddScene(8, "FAIL ME narration", "", "push", SceneVisualType.AiVideo);
        var sceneC = storyboard.AddScene(8, "scene C narration", "", "push", SceneVisualType.AiVideo);

        promptAgent.Respond = input => input.Narration.Contains("FAIL ME")
            ? throw new AgentGenerationException("provider quota exceeded")
            : new PromptAgentOutput("generated action", CameraMovement.Static, null, null);

        var result = await service.SuggestAllScenePromptsAsync(project.Id);

        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.AlreadyPrompted);
        Assert.Equal(3, promptAgent.Calls);
        Assert.Single(result.Errors);
        Assert.Contains("provider quota exceeded", result.Errors[0]);

        // Scenes that already succeeded must keep their generated prompts -
        // the failure of one scene must not roll back the others.
        Assert.Equal("generated action", sceneA.GenerationPrompt);
        Assert.Null(sceneB.GenerationPrompt);
        Assert.Equal("generated action", sceneC.GenerationPrompt);

        Assert.True(project.Progress.IsIdle);
    }

    [Fact]
    public async Task All_scenes_already_prompted_makes_no_AI_calls()
    {
        var project = ContentProject.Create("Bulk Prompt No-Op Test", "topic", "niche", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, _) = Build(project);
        var storyboard = storyboardRepo.Current!;

        var scene1 = storyboard.AddScene(8, "narration 1", "", "push", SceneVisualType.AiVideo);
        scene1.SetGenerationPrompt("prompt 1", null, null, "gemini");
        storyboard.AddScene(8, "narration 2", "written visual description", "push", SceneVisualType.AiVideo);

        var result = await service.SuggestAllScenePromptsAsync(project.Id);

        Assert.Equal(2, result.Total);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, result.AlreadyPrompted);
        Assert.Empty(result.Errors);

        // The cost-safety guarantee this test exists for: zero PromptAgent
        // calls when nothing actually needs a prompt.
        Assert.Equal(0, promptAgent.Calls);

        Assert.True(project.Progress.IsIdle);
    }

    [Fact]
    public async Task Re_suggesting_every_scene_calls_the_agent_once_per_scene_including_prompted_ones()
    {
        var project = ContentProject.Create("Bulk Redo Test", "topic", "niche", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, _) = Build(project);
        var storyboard = storyboardRepo.Current!;
        storyboard.AddScene(8, "narration 1", "", "push", SceneVisualType.AiVideo);
        var prompted = storyboard.AddScene(8, "narration 2", "", "push", SceneVisualType.AiVideo);
        prompted.SetGenerationPrompt("old action", null, null, "gemini");
        promptAgent.Respond = _ => new PromptAgentOutput("new action", CameraMovement.SideTracking, null, null, Shot: ShotSize.Wide);

        var result = await service.SuggestAllScenePromptsAsync(project.Id, includePrompted: true);

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.AlreadyPrompted);
        Assert.Equal(2, promptAgent.Calls);
        Assert.Equal("new action", prompted.GenerationPrompt);
        Assert.Equal(ShotSize.Wide, prompted.ShotSize);
    }

    [Fact]
    public async Task The_default_run_still_never_re_bills_a_prompted_scene()
    {
        var project = ContentProject.Create("Bulk Default Test", "topic", "niche", 60, "9:16", "en");
        var (service, storyboardRepo, promptAgent, _) = Build(project);
        var prompted = storyboardRepo.Current!.AddScene(8, "n", "", "push", SceneVisualType.AiVideo);
        prompted.SetGenerationPrompt("old action", null, null, "gemini");

        await service.SuggestAllScenePromptsAsync(project.Id, includePrompted: false);

        Assert.Equal(0, promptAgent.Calls);
        Assert.Equal("old action", prompted.GenerationPrompt);
    }
}
