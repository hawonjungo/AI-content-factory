using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryContinuityAnalysisAgentTests
{
    private static string ValidResponse() => """
        {"currentLocation":"Bakery doorway","currentObjective":"Find a permanent home",
        "characterStates":"Mimi is exhausted but slightly more trusting of the baker.",
        "importantEvents":["Mimi was chased from the market","Mimi found shelter near a bakery"],
        "openStoryThreads":["Mimi's lost red collar"],
        "unresolvedConflicts":["Mimi still fears the neighborhood dog"],
        "knownFacts":["The baker leaves out scraps every morning"],
        "nextPlannedDestination":"Inside the bakery","notes":"Baker seems kind."}
        """;

    private static StoryContinuityAnalysisAgentInput Input() => new(
        "Lost Kitten Mimi",
        BibleSummary: "Premise: A lonely kitten searches for a home.",
        FinalizedScript: "HOOK:\nA kitten alone in the rain...",
        CurrentLocation: "City alley",
        CurrentObjective: "Find shelter from the rain",
        CharacterStates: "Mimi is tired and wary of humans.",
        ImportantEvents: new[] { "Mimi was chased from the market" },
        OpenStoryThreads: new[] { "Mimi's lost red collar" },
        UnresolvedConflicts: Array.Empty<string>(),
        KnownFacts: Array.Empty<string>(),
        NextPlannedDestination: null,
        Notes: null);

    [Fact]
    public async Task Parses_a_valid_full_state_response_not_a_delta()
    {
        var agent = new StoryContinuityAnalysisAgent(new PassthroughRouter(new StubLlmProvider(ValidResponse())));

        var result = await agent.GenerateAsync(Input());

        Assert.Equal("Bakery doorway", result.CurrentLocation);
        Assert.Equal(2, result.ImportantEvents!.Count); // both the carried-over event and the new one
        Assert.Single(result.OpenStoryThreads!);
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var agent = new StoryContinuityAnalysisAgent(new PassthroughRouter(new StubLlmProvider("nope", "still nope")));

        await Assert.ThrowsAsync<AgentGenerationException>(() => agent.GenerateAsync(Input()));
    }

    [Fact]
    public async Task Missing_list_fields_are_rejected_then_repaired_on_retry()
    {
        var invalid = """{"currentLocation":"x","currentObjective":"y","characterStates":"z","importantEvents":null,"openStoryThreads":null,"unresolvedConflicts":[],"knownFacts":[],"nextPlannedDestination":null,"notes":null}""";
        var stub = new StubLlmProvider(invalid, ValidResponse());
        var agent = new StoryContinuityAnalysisAgent(new PassthroughRouter(stub));

        var result = await agent.GenerateAsync(Input());

        Assert.NotNull(result.ImportantEvents);
        Assert.Equal(2, stub.Calls);
    }
}
