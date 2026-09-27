using AiContentFactory.Application.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers;

/// <summary>DI keys for the two <see cref="ILlmProvider"/> registrations <see cref="LlmRouter"/> chooses between.</summary>
public static class LlmProviderKeys
{
    public const string Gemini = "Gemini";
    public const string Groq = "Groq";
}

/// <summary>
/// Resolves the configured primary/fallback <see cref="ILlmProvider"/> pair for
/// one pipeline step (see <see cref="LlmRoutingOptions"/>). Never routes to a
/// provider whose API key isn't configured - that provider is simply skipped
/// rather than presented as available, per the "don't fake provider
/// availability" rule. With no Llm:Routing config and no Groq key set, every
/// task resolves straight to Gemini, unchanged from before this existed.
/// </summary>
public class LlmRouter : ILlmRouter
{
    private readonly ILlmProvider _gemini;
    private readonly ILlmProvider _groq;
    private readonly LlmRoutingOptions _options;
    private readonly ILogger<LlmRouter> _logger;

    public LlmRouter(
        [FromKeyedServices(LlmProviderKeys.Gemini)] ILlmProvider gemini,
        [FromKeyedServices(LlmProviderKeys.Groq)] ILlmProvider groq,
        IOptions<LlmRoutingOptions> options,
        ILogger<LlmRouter> logger)
    {
        _gemini = gemini;
        _groq = groq;
        _options = options.Value;
        _logger = logger;
    }

    public ILlmProvider Resolve(LlmTaskType task)
    {
        var route = task switch
        {
            LlmTaskType.Ideas => _options.Ideas,
            LlmTaskType.HookTitle => _options.HookTitle,
            LlmTaskType.Script => _options.Script,
            _ => new LlmRouteOptions()
        };

        var primary = UsableOrNull(ByName(route.Primary)) ?? _gemini;
        var fallback = UsableOrNull(ByName(route.Fallback));
        if (fallback is not null && ReferenceEquals(fallback, primary))
        {
            fallback = null;
        }

        return fallback is null ? primary : new FallbackProvider(primary, fallback, task, _logger);
    }

    private ILlmProvider? ByName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "gemini" => _gemini,
        "groq" => _groq,
        _ => null
    };

    private static ILlmProvider? UsableOrNull(ILlmProvider? provider) =>
        provider is { IsConfigured: true } ? provider : null;

    /// <summary>Tries <paramref name="primary"/>; on any failure, logs and tries <paramref name="fallback"/> once. A fallback failure propagates as-is.</summary>
    private sealed class FallbackProvider : ILlmProvider
    {
        private readonly ILlmProvider _primary;
        private readonly ILlmProvider _fallback;
        private readonly LlmTaskType _task;
        private readonly ILogger _logger;

        public FallbackProvider(ILlmProvider primary, ILlmProvider fallback, LlmTaskType task, ILogger logger)
        {
            _primary = primary;
            _fallback = fallback;
            _task = task;
            _logger = logger;
        }

        public bool IsConfigured => true;

        public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _primary.GenerateAsync(systemPrompt, userPrompt, cancellationToken);
            }
            // A caller-requested cancellation isn't a provider failure - propagate it as-is rather than burning a fallback call.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "LLM task {Task}: primary provider {Primary} failed, falling back to {Fallback}",
                    _task, _primary.GetType().Name, _fallback.GetType().Name);
                return await _fallback.GenerateAsync(systemPrompt, userPrompt, cancellationToken);
            }
        }
    }
}
