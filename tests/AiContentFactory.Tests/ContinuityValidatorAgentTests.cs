using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class ContinuityValidatorAgentTests
{
    private static string ValidResponse(bool hasCriticalIssue = false) => hasCriticalIssue
        ? """{"warnings":[],"criticalIssues":[{"category":"Location","type":"IMPOSSIBLE_LOCATION_TRANSITION","message":"Mimi is in the bakery with no explanation for leaving the alley."}],"score":0.4}"""
        : """{"warnings":[],"criticalIssues":[],"score":0.95}""";

    private static ContinuityValidatorAgentInput Input() => new(
        "Lost Kitten Mimi",
        BibleSummary: "Premise: A lonely kitten searches for a home.",
        EpisodeContent: "HOOK:\nA kitten alone in the rain...",
        CurrentLocation: "City alley",
        CurrentObjective: "Find shelter from the rain",
        CharacterStates: "Mimi is tired and wary of humans.",
        OpenStoryThreads: new[] { "Mimi's lost red collar" },
        UnresolvedConflicts: Array.Empty<string>(),
        KnownFacts: Array.Empty<string>());

    [Fact]
    public async Task Parses_a_response_with_no_issues()
    {
        var agent = new ContinuityValidatorAgent(new PassthroughRouter(new StubLlmProvider(ValidResponse(false))));

        var result = await agent.GenerateAsync(Input());

        Assert.True(result.Warnings is null || result.Warnings.Count == 0);
        Assert.True(result.CriticalIssues is null || result.CriticalIssues.Count == 0);
        Assert.Equal(0.95, result.Score);
    }

    [Fact]
    public async Task Parses_a_response_with_a_categorized_critical_issue()
    {
        var agent = new ContinuityValidatorAgent(new PassthroughRouter(new StubLlmProvider(ValidResponse(true))));

        var result = await agent.GenerateAsync(Input());

        Assert.Single(result.CriticalIssues!);
        Assert.Equal(ContinuityCategories.Location, result.CriticalIssues![0].Category);
        Assert.Equal(ContinuityIssueTypes.ImpossibleLocationTransition, result.CriticalIssues![0].Type);
        Assert.True(result.Warnings is null || result.Warnings.Count == 0);
    }

    [Fact]
    public async Task System_prompt_lists_the_closed_category_and_type_taxonomy()
    {
        var stub = new StubLlmProvider(ValidResponse(false));
        var agent = new ContinuityValidatorAgent(new PassthroughRouter(stub));

        await agent.GenerateAsync(Input());

        foreach (var category in ContinuityCategories.All)
        {
            Assert.Contains(category, stub.LastSystemPrompt);
        }

        foreach (var types in ContinuityIssueTypes.ByCategory.Values)
        {
            foreach (var type in types)
            {
                Assert.Contains(type, stub.LastSystemPrompt);
            }
        }
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var agent = new ContinuityValidatorAgent(new PassthroughRouter(new StubLlmProvider("nope", "still nope")));

        await Assert.ThrowsAsync<AgentGenerationException>(() => agent.GenerateAsync(Input()));
    }

    [Fact]
    public async Task Issue_with_blank_message_is_rejected_then_repaired_on_retry()
    {
        var invalid = """{"warnings":[{"category":"Other","type":"OTHER","message":""}],"criticalIssues":[],"score":0.5}""";
        var stub = new StubLlmProvider(invalid, ValidResponse(true));
        var agent = new ContinuityValidatorAgent(new PassthroughRouter(stub));

        var result = await agent.GenerateAsync(Input());

        Assert.NotEmpty(result.CriticalIssues![0].Message);
        Assert.Equal(2, stub.Calls);
    }

    [Fact]
    public async Task Issue_with_an_unknown_category_is_rejected_then_repaired_on_retry()
    {
        var invalid = """{"warnings":[{"category":"NotACategory","type":"OTHER","message":"something"}],"criticalIssues":[],"score":0.5}""";
        var stub = new StubLlmProvider(invalid, ValidResponse(false));
        var agent = new ContinuityValidatorAgent(new PassthroughRouter(stub));

        var result = await agent.GenerateAsync(Input());

        Assert.True(result.Warnings is null || result.Warnings.Count == 0);
        Assert.Equal(2, stub.Calls);
    }
}
