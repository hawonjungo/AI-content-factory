using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="ClipPlanService"/>'s deterministic, no-AI scene tagging:
/// for a Story-linked project, each newly-created scene's narration is
/// scanned for the Story's own character/location names (see
/// <see cref="IStoryVisualContextResolver.GetStoryCastAndLocationNamesAsync"/>)
/// and <see cref="Scene.SetRelevantReferenceLabels"/> is called with any
/// match. A non-Story project (the resolver returns null) must see zero
/// change from today's behavior.
/// </summary>
public class ClipPlanServiceSceneTaggingTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static ScriptResponse Script(string hook, string body, string payoff) => new(
        Guid.NewGuid(), Guid.NewGuid(),
        Hook: hook,
        Introduction: string.Empty,
        Body: body,
        Escalation: string.Empty,
        Payoff: payoff,
        CallToAction: string.Empty,
        DateTimeOffset.UtcNow);

    private ClipPlanService Build(ScriptResponse script, IReadOnlyList<string>? storyReferenceNames)
    {
        var project = ContentProject.Create("t", "topic", "niche", 60, "9:16", "en");
        project.UpdateIdeaConfig(ContentIdeaConfig.Create(
            null, null, null, null, null, VoiceGender.Unspecified, null, null, null, CreditStrategy.Balanced));

        return new ClipPlanService(
            new FakeScriptService(script),
            new FakeStoryboardRepository(Storyboard.Create(_project)),
            new FakeContentProjectRepository(project),
            new VideoAllocationPlanner(),
            new FakeStoryVisualContextResolver { CastAndLocationNames = storyReferenceNames },
            Options.Create(new CreditCostOptions()));
    }

    [Fact]
    public async Task Story_linked_project_tags_each_scene_with_the_names_its_narration_mentions()
    {
        var script = Script(
            hook: "Milo spotted a hidden cave near the shore.",
            body: "The travelers reached Hoi An Ancient Town by dusk.",
            payoff: "Nobody could believe what happened next.");

        var service = Build(script, new[] { "Milo", "Hoi An Ancient Town" });

        var storyboard = await service.GenerateAsync(_project, new GenerateClipPlanRequest(3, 8));
        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();

        Assert.Equal(3, scenes.Count);
        Assert.Equal(new[] { "Milo" }, scenes[0].RelevantReferenceLabels);
        Assert.Equal(new[] { "Hoi An Ancient Town" }, scenes[1].RelevantReferenceLabels);
        Assert.Empty(scenes[2].RelevantReferenceLabels);
    }

    [Fact]
    public async Task Word_boundary_matching_does_not_false_match_a_short_name_inside_an_unrelated_word()
    {
        var script = Script(
            hook: "Milo waved from the dock.",
            body: "The ancient temple stood tall over the valley.",
            payoff: "The crew moved on at dawn.");

        // "An" is a real (if contrived) location name here - it must not
        // match inside "ancient", only as its own standalone word.
        var service = Build(script, new[] { "Milo", "An" });

        var storyboard = await service.GenerateAsync(_project, new GenerateClipPlanRequest(3, 8));
        var scenes = storyboard.Scenes.OrderBy(s => s.SceneNumber).ToList();

        Assert.Equal(new[] { "Milo" }, scenes[0].RelevantReferenceLabels);
        Assert.Empty(scenes[1].RelevantReferenceLabels); // "ancient" must not false-match "An"
        Assert.Empty(scenes[2].RelevantReferenceLabels);
    }

    [Fact]
    public async Task Non_story_project_leaves_every_scene_untagged()
    {
        var script = Script(
            hook: "Milo spotted a hidden cave near the shore.",
            body: "The travelers reached Hoi An Ancient Town by dusk.",
            payoff: "Nobody could believe what happened next.");

        // Null CastAndLocationNames == "not a Story-linked project", the
        // default for every existing/normal project.
        var service = Build(script, storyReferenceNames: null);

        var storyboard = await service.GenerateAsync(_project, new GenerateClipPlanRequest(3, 8));

        Assert.All(storyboard.Scenes, s => Assert.Empty(s.RelevantReferenceLabels));
    }
}
