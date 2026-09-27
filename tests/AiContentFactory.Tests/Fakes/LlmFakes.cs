using AiContentFactory.Application.Providers;

namespace AiContentFactory.Tests.Fakes;

/// <summary>
/// Shared ILlmProvider test double: returns queued canned responses in
/// order, repeating the last one once the queue drains (or throws, when
/// configured to). Reused across every Continuity agent test so each test
/// file doesn't redefine its own copy - mirrors the pattern already used
/// locally in ContentIdeasServiceTests/AssetReferencePromptAgentTests.
/// </summary>
public sealed class StubLlmProvider : ILlmProvider
{
    private readonly Queue<string> _responses;
    public int Calls { get; private set; }
    public string? LastUserPrompt { get; private set; }
    public string? LastSystemPrompt { get; private set; }
    public bool IsConfigured => true;

    /// <summary>When set, every call throws this instead of returning a response.</summary>
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

/// <summary>Always resolves to the single stub provider under test, ignoring task type - these tests exercise agents/services, not routing.</summary>
public sealed class PassthroughRouter : ILlmRouter
{
    private readonly ILlmProvider _provider;
    public PassthroughRouter(ILlmProvider provider) => _provider = provider;
    public ILlmProvider Resolve(LlmTaskType task) => _provider;
}
