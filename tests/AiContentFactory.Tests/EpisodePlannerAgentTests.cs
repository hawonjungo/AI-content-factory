using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class EpisodePlannerAgentTests
{
    private static string ValidResponse() => """
        {"title":"The Alley Cat's Gamble","objective":"Mimi finds temporary shelter.",
        "setup":"Rain forces Mimi to seek cover near a bakery.",
        "majorBeats":["Mimi spots warm light","A baker notices her","Mimi hesitates to trust"],
        "conflict":"Mimi fears getting too close to humans again.",
        "escalation":"The baker's dog barks, scaring Mimi off.",
        "resolution":"Mimi finds a dry box to sleep in for the night.",
        "cliffhanger":"A stranger's collar tag glints nearby - familiar to Mimi's lost one.",
        "continuityRequirements":["Must not resolve Mimi's search for a home yet."],
        "scenesRequired":["Rainy alley","Bakery window","Dry box interior"]}
        """;

    private static EpisodePlannerAgentInput Input() => new(
        "Lost Kitten Mimi",
        EpisodeNumber: 3,
        BibleSummary: "Premise: A lonely kitten searches for a home.",
        CurrentStoryArc: "Mimi's season-long journey to find a forever home.",
        CurrentLocation: "City alley",
        CurrentObjective: "Find shelter from the rain",
        OpenStoryThreads: new[] { "Mimi's lost red collar" },
        PreviousEpisodeSummary: "Mimi was chased away from the market.",
        PreviousEpisodeEnding: "Mimi ran into a dark alley, exhausted.");

    [Fact]
    public async Task Parses_a_valid_outline_response()
    {
        var agent = new EpisodePlannerAgent(new PassthroughRouter(new StubLlmProvider(ValidResponse())));

        var result = await agent.GenerateAsync(Input());

        Assert.Equal("The Alley Cat's Gamble", result.Title);
        Assert.Equal(3, result.MajorBeats!.Count);
        Assert.NotEmpty(result.Cliffhanger!);
    }

    [Fact]
    public async Task User_prompt_carries_the_previous_episode_ending_and_open_threads_not_a_full_script()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new EpisodePlannerAgent(new PassthroughRouter(stub));

        await agent.GenerateAsync(Input());

        Assert.Contains("Mimi ran into a dark alley", stub.LastUserPrompt);
        Assert.Contains("Mimi's lost red collar", stub.LastUserPrompt);
        Assert.Contains("episode 3", stub.LastUserPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var agent = new EpisodePlannerAgent(new PassthroughRouter(new StubLlmProvider("nope", "still nope")));

        await Assert.ThrowsAsync<AgentGenerationException>(() => agent.GenerateAsync(Input()));
    }

    [Fact]
    public async Task Missing_major_beats_is_rejected_then_repaired_on_retry()
    {
        var invalid = """{"title":"t","objective":"o","setup":"s","majorBeats":[],"conflict":"c","escalation":"e","resolution":"r","cliffhanger":"cl","continuityRequirements":[],"scenesRequired":[]}""";
        var stub = new StubLlmProvider(invalid, ValidResponse());
        var agent = new EpisodePlannerAgent(new PassthroughRouter(stub));

        var result = await agent.GenerateAsync(Input());

        Assert.NotEmpty(result.MajorBeats!);
        Assert.Equal(2, stub.Calls);
    }
}
