using AiContentFactory.Application.Providers;
using AiContentFactory.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class LlmRouterTests
{
    private sealed class FakeProvider : ILlmProvider
    {
        public bool IsConfigured { get; init; } = true;
        public int Calls { get; private set; }
        public Func<string>? Response { get; init; }
        public Exception? Throws { get; init; }

        public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throws is not null)
            {
                throw Throws;
            }
            return Task.FromResult(Response?.Invoke() ?? "ok");
        }
    }

    private static LlmRouter Build(FakeProvider gemini, FakeProvider groq, LlmRoutingOptions? options = null) =>
        new(gemini, groq, Options.Create(options ?? new LlmRoutingOptions()), NullLogger<LlmRouter>.Instance);

    [Fact]
    public async Task With_no_routing_config_every_task_resolves_to_Gemini()
    {
        var gemini = new FakeProvider();
        var groq = new FakeProvider();
        var router = Build(gemini, groq); // default LlmRoutingOptions: Primary = "Gemini" for every task, no Fallback

        await router.Resolve(LlmTaskType.Ideas).GenerateAsync("s", "u");
        await router.Resolve(LlmTaskType.Script).GenerateAsync("s", "u");
        await router.Resolve(LlmTaskType.HookTitle).GenerateAsync("s", "u");

        Assert.Equal(3, gemini.Calls);
        Assert.Equal(0, groq.Calls);
    }

    [Fact]
    public async Task Configured_primary_is_used_when_it_succeeds()
    {
        var gemini = new FakeProvider();
        var groq = new FakeProvider();
        var options = new LlmRoutingOptions { Ideas = new LlmRouteOptions { Primary = "Groq" } };
        var router = Build(gemini, groq, options);

        await router.Resolve(LlmTaskType.Ideas).GenerateAsync("s", "u");

        Assert.Equal(1, groq.Calls);
        Assert.Equal(0, gemini.Calls);
    }

    [Fact]
    public async Task An_unconfigured_primary_falls_back_to_Gemini_even_without_an_explicit_Fallback()
    {
        var gemini = new FakeProvider();
        var groq = new FakeProvider { IsConfigured = false }; // e.g. no GROQ_API_KEY set
        var options = new LlmRoutingOptions { Ideas = new LlmRouteOptions { Primary = "Groq" } };
        var router = Build(gemini, groq, options);

        await router.Resolve(LlmTaskType.Ideas).GenerateAsync("s", "u");

        Assert.Equal(1, gemini.Calls);
        Assert.Equal(0, groq.Calls); // never called - "unconfigured" must not mean "used anyway"
    }

    [Fact]
    public async Task A_failing_primary_falls_back_to_the_configured_fallback_provider()
    {
        var gemini = new FakeProvider { Response = () => "from gemini" };
        var groq = new FakeProvider { Throws = new LlmQuotaExceededException("rate limited") };
        var options = new LlmRoutingOptions { Ideas = new LlmRouteOptions { Primary = "Groq", Fallback = "Gemini" } };
        var router = Build(gemini, groq, options);

        var result = await router.Resolve(LlmTaskType.Ideas).GenerateAsync("s", "u");

        Assert.Equal("from gemini", result);
        Assert.Equal(1, groq.Calls);
        Assert.Equal(1, gemini.Calls);
    }

    [Fact]
    public async Task When_both_primary_and_fallback_fail_the_fallbacks_exception_propagates()
    {
        var gemini = new FakeProvider { Throws = new InvalidOperationException("gemini also down") };
        var groq = new FakeProvider { Throws = new LlmQuotaExceededException("rate limited") };
        var options = new LlmRoutingOptions { Ideas = new LlmRouteOptions { Primary = "Groq", Fallback = "Gemini" } };
        var router = Build(gemini, groq, options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.Resolve(LlmTaskType.Ideas).GenerateAsync("s", "u"));

        Assert.Equal("gemini also down", ex.Message);
    }

    [Fact]
    public async Task A_cancellation_from_the_primary_is_not_treated_as_a_failure_to_fall_back_from()
    {
        var gemini = new FakeProvider();
        var groq = new FakeProvider { Throws = new OperationCanceledException() };
        var options = new LlmRoutingOptions { Ideas = new LlmRouteOptions { Primary = "Groq", Fallback = "Gemini" } };
        var router = Build(gemini, groq, options);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => router.Resolve(LlmTaskType.Ideas).GenerateAsync("s", "u"));

        Assert.Equal(0, gemini.Calls); // never used as a fallback for a cancellation
    }
}
