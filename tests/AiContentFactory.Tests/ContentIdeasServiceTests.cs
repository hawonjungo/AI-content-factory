using System.Text;
using System.Text.Json;
using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Ideas;
using AiContentFactory.Application.Providers;
using Xunit;

namespace AiContentFactory.Tests;

public class ContentIdeasServiceTests
{
    /// <summary>Returns queued responses in order, repeating the last one once the queue drains, or throws.</summary>
    private sealed class StubLlmProvider : ILlmProvider
    {
        private readonly Queue<string> _responses;
        public int Calls { get; private set; }
        public string? LastUserPrompt { get; private set; }
        public string? LastSystemPrompt { get; private set; }

        public bool IsConfigured => true;

        /// <summary>When set, every call throws this instead of returning.</summary>
        public Exception? Throws { get; init; }

        public StubLlmProvider(params string[] responses)
        {
            _responses = new Queue<string>(responses.Length == 0 ? new[] { "{}" } : responses);
        }

        public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;
            if (Throws is not null)
            {
                throw Throws;
            }

            var next = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return Task.FromResult(next);
        }
    }

    /// <summary>Always resolves to the single stub provider under test, ignoring task type - these tests exercise ContentIdeasAgent/Service, not routing.</summary>
    private sealed class PassthroughRouter : ILlmRouter
    {
        private readonly ILlmProvider _provider;
        public PassthroughRouter(ILlmProvider provider) => _provider = provider;
        public ILlmProvider Resolve(LlmTaskType task) => _provider;
    }

    private static ContentIdeasService Build(ILlmProvider llm) => new(new ContentIdeasAgent(new PassthroughRouter(llm)));

    /// <summary>Builds a valid N-idea payload; per-index score overrides let a test control scores. The agent's fixed count is 1 - pass a different count to build a deliberately-wrong payload for the repair-retry test.</summary>
    private static string IdeasPayload(
        int count = 1,
        Func<int, int>? monetization = null,
        Func<int, int>? viral = null,
        Func<int, int>? overall = null)
    {
        monetization ??= i => 50;
        viral ??= i => 50;
        overall ??= i => 50;

        var sb = new StringBuilder();
        sb.Append("{\"ideas\":[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('{')
              .Append($"\"rank\":{i + 1},")
              .Append($"\"title\":\"Idea {i}\",")
              .Append("\"niche\":\"AI\",")
              .Append($"\"concept\":\"Concept number {i} about a thing.\",")
              .Append($"\"hook\":\"Hook line {i}.\",")
              .Append("\"why_it_works\":\"Because it is interesting.\",")
              .Append($"\"monetization_score\":{monetization(i)},")
              .Append($"\"viral_score\":{viral(i)},")
              .Append($"\"overall_score\":{overall(i)}")
              .Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    /// <summary>Builds a valid 1-idea payload (the agent's fixed count).</summary>
    private static string OneIdea(
        Func<int, int>? monetization = null,
        Func<int, int>? viral = null,
        Func<int, int>? overall = null) =>
        IdeasPayload(1, monetization, viral, overall);

    [Fact]
    public async Task Returns_exactly_one_idea_ranked_first()
    {
        var service = Build(new StubLlmProvider(OneIdea()));

        var result = await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        Assert.Single(result.Ideas);
        Assert.Equal(1, result.Ideas[0].Rank);
        Assert.Equal("overall", result.Goal);
        Assert.False(string.IsNullOrWhiteSpace(result.Disclaimer));
        Assert.DoesNotContain("trend", result.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_idea_has_the_required_fields_and_in_range_scores()
    {
        var service = Build(new StubLlmProvider(OneIdea()));

        var result = await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        Assert.All(result.Ideas, idea =>
        {
            Assert.False(string.IsNullOrWhiteSpace(idea.Title));
            Assert.False(string.IsNullOrWhiteSpace(idea.Niche));
            Assert.False(string.IsNullOrWhiteSpace(idea.Concept));
            Assert.False(string.IsNullOrWhiteSpace(idea.Hook));
            Assert.False(string.IsNullOrWhiteSpace(idea.WhyItWorks));
            Assert.InRange(idea.MonetizationScore, 0, 100);
            Assert.InRange(idea.ViralScore, 0, 100);
            Assert.InRange(idea.OverallScore, 0, 100);
        });
    }

    [Fact]
    public async Task Goal_is_reflected_in_the_response_for_every_ranking_objective()
    {
        var service = Build(new StubLlmProvider(OneIdea()));

        var overall = await service.SuggestAsync(new ContentIdeaSuggestionsRequest(Goal: ContentIdeaGoal.Overall));
        var money = await service.SuggestAsync(new ContentIdeaSuggestionsRequest(Goal: ContentIdeaGoal.Monetization));
        var views = await service.SuggestAsync(new ContentIdeaSuggestionsRequest(Goal: ContentIdeaGoal.Views));

        Assert.Equal("overall", overall.Goal);
        Assert.Equal("monetization", money.Goal);
        Assert.Equal("views", views.Goal);
    }

    [Fact]
    public async Task System_prompt_asks_for_a_single_best_idea_not_a_list()
    {
        var stub = new StubLlmProvider(OneIdea());
        var service = Build(stub);

        await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        Assert.Contains("select and return ONLY the single strongest idea", stub.LastSystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXACTLY 1 idea", stub.LastSystemPrompt);
    }

    [Fact]
    public async Task Json_wrapped_in_a_markdown_fence_is_still_parsed()
    {
        var fenced = "```json\n" + OneIdea() + "\n```";
        var service = Build(new StubLlmProvider(fenced));

        var result = await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        Assert.Single(result.Ideas);
    }

    [Fact]
    public async Task Malformed_json_after_retry_throws_AgentGenerationException()
    {
        var service = Build(new StubLlmProvider("this is not json", "still not json"));

        await Assert.ThrowsAsync<AgentGenerationException>(
            () => service.SuggestAsync(new ContentIdeaSuggestionsRequest()));
    }

    [Fact]
    public async Task Wrong_idea_count_is_rejected_then_repaired_on_retry()
    {
        var three = IdeasPayload(3);
        var stub = new StubLlmProvider(three, OneIdea());
        var service = Build(stub);

        var result = await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        Assert.Single(result.Ideas);
        Assert.Equal(2, stub.Calls); // first rejected, repair prompt succeeded
    }

    [Fact]
    public async Task Provider_quota_failure_propagates_and_is_not_retried()
    {
        var stub = new StubLlmProvider { Throws = new LlmQuotaExceededException("Gemini API returned 429: spending cap") };
        var service = Build(stub);

        await Assert.ThrowsAsync<LlmQuotaExceededException>(
            () => service.SuggestAsync(new ContentIdeaSuggestionsRequest()));

        Assert.Equal(1, stub.Calls); // no wasteful repair retry on a quota failure
    }

    [Fact]
    public async Task Out_of_range_score_is_rejected_by_validation()
    {
        var bad = OneIdea(monetization: _ => 150);
        var service = Build(new StubLlmProvider(bad, bad));

        await Assert.ThrowsAsync<AgentGenerationException>(
            () => service.SuggestAsync(new ContentIdeaSuggestionsRequest()));
    }

    [Fact]
    public async Task Request_is_normalised_before_it_reaches_the_model()
    {
        var stub = new StubLlmProvider(OneIdea());
        var service = Build(stub);

        await service.SuggestAsync(new ContentIdeaSuggestionsRequest(
            Goal: ContentIdeaGoal.Overall,
            Niche: "auto",
            Platforms: null,
            Language: "vi",
            Duration: 0,
            ExistingIdeas: new[] { "  ", "Real idea", "Real idea" }));

        var prompt = stub.LastUserPrompt!;
        Assert.Contains("discover the best-fitting niches", prompt);   // "auto" -> discover
        Assert.Contains("60 seconds", prompt);                          // duration 0 -> clamped default
        Assert.Contains("Vietnamese", prompt);                          // language spelled out
        Assert.Contains("TikTok", prompt);                              // default platforms filled in
        Assert.Contains("- Real idea", prompt);                         // trimmed + de-duplicated
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(prompt, "- Real idea"));
    }

    [Fact]
    public async Task Response_serialises_with_the_camelCase_keys_the_frontend_expects()
    {
        var service = Build(new StubLlmProvider(OneIdea()));
        var result = await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        // Same options ASP.NET Core uses for controller output.
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"ideas\":", json);
        Assert.Contains("\"goal\":\"overall\"", json);
        Assert.Contains("\"disclaimer\":", json);
        Assert.Contains("\"rank\":", json);
        Assert.Contains("\"whyItWorks\":", json);
        Assert.Contains("\"monetizationScore\":", json);
        Assert.Contains("\"viralScore\":", json);
        Assert.Contains("\"overallScore\":", json);
    }

    [Fact]
    public async Task No_existing_ideas_tells_the_model_to_research_the_space()
    {
        var stub = new StubLlmProvider(OneIdea());
        var service = Build(stub);

        await service.SuggestAsync(new ContentIdeaSuggestionsRequest());

        Assert.Contains("the user has no ideas yet", stub.LastUserPrompt!);
    }
}
