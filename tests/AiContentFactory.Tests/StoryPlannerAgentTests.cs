using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryPlannerAgentTests
{
    private static string ValidResponse() => """
        {"premise":"A lonely kitten searches the city for a home.","worldRules":["Animals can be adopted by any kind stranger."],
        "characterDefinitions":"Mimi is a small orange tabby kitten, curious and brave despite her fear.",
        "characterRelationships":"Mimi trusts no one yet, but yearns for connection.",
        "visualConsistencyRules":["Mimi always has a torn left ear."],
        "tone":"Bittersweet and hopeful.",
        "recurringElements":["A red collar Mimi lost in episode one."],
        "storyConstraints":["Mimi must never speak human language."],
        "storyArc":"Mimi's season-long journey to find a forever home."}
        """;

    private static StoryPlannerAgentInput Input() => new(
        "Lost Kitten Mimi", "found family", "slice of life", "warm", "general audience",
        MainCharacters: new[] { "Mimi: an orange tabby kitten" },
        Locations: new[] { "City alley" },
        StoryRulesConstraints: "Keep it kid-friendly",
        DesiredEpisodeCount: 10);

    [Fact]
    public async Task Parses_a_valid_bible_response()
    {
        var agent = new StoryPlannerAgent(new PassthroughRouter(new StubLlmProvider(ValidResponse())));

        var result = await agent.GenerateAsync(Input());

        Assert.Contains("kitten", result.Premise, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.WorldRules!);
        Assert.NotNull(result.StoryArc);
        Assert.NotEmpty(result.VisualConsistencyRules!);
    }

    [Fact]
    public async Task Story_arc_can_be_explicitly_null_for_open_ended_stories()
    {
        var openEnded = """
            {"premise":"Weekly life-hack episodes.","worldRules":[],
            "characterDefinitions":"Host: an upbeat narrator.","characterRelationships":"N/A",
            "visualConsistencyRules":[],"tone":"Energetic.","recurringElements":[],"storyConstraints":[],"storyArc":null}
            """;
        var agent = new StoryPlannerAgent(new PassthroughRouter(new StubLlmProvider(openEnded)));

        var result = await agent.GenerateAsync(Input() with { DesiredEpisodeCount = null });

        Assert.Null(result.StoryArc);
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var agent = new StoryPlannerAgent(new PassthroughRouter(new StubLlmProvider("not json", "still not json")));

        await Assert.ThrowsAsync<AgentGenerationException>(() => agent.GenerateAsync(Input()));
    }

    /// <summary>
    /// Belt-and-suspenders reinforcement (the root cause was the missing
    /// data upstream in StoryContinuityManager, fixed separately): the
    /// system prompt must explicitly forbid inventing a contradictory
    /// appearance once one is explicitly given, and a given "(Appearance:
    /// ...)" line must reach the user prompt verbatim.
    /// </summary>
    [Fact]
    public async Task System_prompt_forbids_inventing_a_contradictory_appearance_and_the_given_appearance_reaches_the_user_prompt()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new StoryPlannerAgent(new PassthroughRouter(stub));

        await agent.GenerateAsync(Input() with
        {
            MainCharacters = new[] { "Milo: A curious housecat. (Appearance: grey short-haired cat, amber eyes)" }
        });

        Assert.Contains("you MUST reuse that exact", stub.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never invent, alter, or contradict a", stub.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grey short-haired cat, amber eyes", stub.LastUserPrompt);
    }

    [Fact]
    public async Task Missing_premise_is_rejected_then_repaired_on_retry()
    {
        var invalid = """{"premise":"","worldRules":[],"characterDefinitions":"x","characterRelationships":"y","visualConsistencyRules":[],"tone":"z","recurringElements":[],"storyConstraints":[],"storyArc":null}""";
        var stub = new StubLlmProvider(invalid, ValidResponse());
        var agent = new StoryPlannerAgent(new PassthroughRouter(stub));

        var result = await agent.GenerateAsync(Input());

        Assert.NotEmpty(result.Premise!);
        Assert.Equal(2, stub.Calls);
    }
}
