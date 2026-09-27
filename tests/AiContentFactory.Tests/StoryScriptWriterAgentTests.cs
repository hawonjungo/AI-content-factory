using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryScriptWriterAgentTests
{
    private static string ValidResponse() => """
        {"hook":"A kitten alone in the rain has nowhere left to run.",
        "introduction":"Mimi's paws ache from the cold cobblestones.",
        "body":"She spots warm light spilling from a bakery window.",
        "escalation":"A dog's bark sends her fleeing into the dark.",
        "payoff":"She curls up safe in a dry cardboard box.",
        "callToAction":"Follow to see if Mimi finds her way home.",
        "summary":"Mimi sought shelter from the rain, was chased off by a dog, and found a dry box to sleep in."}
        """;

    private static StoryScriptWriterAgentInput Input() => new(
        "Lost Kitten Mimi",
        BibleSummary: "Premise: A lonely kitten searches for a home.",
        VisualConsistencyRules: "Mimi always has a torn left ear.",
        CharacterDefinitions: "Mimi is a small orange tabby kitten, curious and brave.",
        CurrentLocation: "City alley",
        CurrentObjective: "Find shelter from the rain",
        CharacterStates: "Mimi is tired and wary of humans.",
        PreviousEpisodeSummary: "Mimi was chased away from the market.",
        OutlineSummary: "Objective: Mimi finds temporary shelter.",
        Language: "en");

    [Fact]
    public async Task Parses_a_valid_script_response_shaped_like_ScriptAgentOutput()
    {
        var agent = new StoryScriptWriterAgent(new PassthroughRouter(new StubLlmProvider(ValidResponse())));

        var result = await agent.GenerateAsync(Input());

        Assert.Contains("kitten", result.Hook, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Introduction);
        Assert.NotEmpty(result.Body);
        Assert.NotEmpty(result.Escalation);
        Assert.NotEmpty(result.Payoff);
        Assert.NotEmpty(result.CallToAction);
        Assert.Contains("dry box", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task User_prompt_carries_bible_visual_rules_and_outline_not_a_full_prior_script()
    {
        var stub = new StubLlmProvider(ValidResponse());
        var agent = new StoryScriptWriterAgent(new PassthroughRouter(stub));

        await agent.GenerateAsync(Input());

        Assert.Contains("torn left ear", stub.LastUserPrompt);
        Assert.Contains("Objective: Mimi finds temporary shelter.", stub.LastUserPrompt);
        Assert.Contains("chased away from the market", stub.LastUserPrompt);
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var agent = new StoryScriptWriterAgent(new PassthroughRouter(new StubLlmProvider("nope", "still nope")));

        await Assert.ThrowsAsync<AgentGenerationException>(() => agent.GenerateAsync(Input()));
    }

    [Fact]
    public async Task Missing_hook_is_rejected_then_repaired_on_retry()
    {
        var invalid = """{"hook":"","introduction":"i","body":"b","escalation":"e","payoff":"p","callToAction":"c","summary":"s"}""";
        var stub = new StubLlmProvider(invalid, ValidResponse());
        var agent = new StoryScriptWriterAgent(new PassthroughRouter(stub));

        var result = await agent.GenerateAsync(Input());

        Assert.NotEmpty(result.Hook);
        Assert.Equal(2, stub.Calls);
    }
}
