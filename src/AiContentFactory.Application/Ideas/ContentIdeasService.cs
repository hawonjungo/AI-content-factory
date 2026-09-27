using AiContentFactory.Application.Agents;

namespace AiContentFactory.Application.Ideas;

/// <summary>Ranking objective the user picks before generating. Default is <see cref="Overall"/>.</summary>
public enum ContentIdeaGoal
{
    Overall = 0,
    Monetization = 1,
    Views = 2
}

/// <param name="Goal">Ranking objective. Omitted =&gt; Overall.</param>
/// <param name="Niche">A niche name, or null / "auto" / "discover" to let the AI choose.</param>
/// <param name="Platforms">Short-form platforms. Omitted =&gt; TikTok + Reels + Shorts.</param>
/// <param name="Language">Project language code ("en", "vi", ...). Omitted =&gt; "en".</param>
/// <param name="Duration">Target finished-video length in seconds. Omitted / &lt;= 0 =&gt; 60.</param>
/// <param name="ExistingIdeas">Ideas the user already has, plus any previous suggestions to avoid repeating.</param>
public record ContentIdeaSuggestionsRequest(
    ContentIdeaGoal Goal = ContentIdeaGoal.Overall,
    string? Niche = null,
    IReadOnlyList<string>? Platforms = null,
    string? Language = null,
    int Duration = 60,
    IReadOnlyList<string>? ExistingIdeas = null);

public record ContentIdeaSuggestionDto(
    int Rank,
    string Title,
    string Niche,
    string Concept,
    string Hook,
    string WhyItWorks,
    int MonetizationScore,
    int ViralScore,
    int OverallScore);

/// <param name="Disclaimer">
/// Honest framing shown in the UI. The app has no real-time trend feed, so this
/// never claims the ideas are "currently trending".
/// </param>
public record ContentIdeaSuggestionsResponse(
    IReadOnlyList<ContentIdeaSuggestionDto> Ideas,
    string Goal,
    string Disclaimer);

public interface IContentIdeasService
{
    Task<ContentIdeaSuggestionsResponse> SuggestAsync(ContentIdeaSuggestionsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Normalises the request, calls <see cref="IContentIdeasAgent"/>, then applies a
/// deterministic goal-weighted re-rank on top of the model's own scores so the
/// order isn't left to the model alone. Idea-level only - no scripts.
/// </summary>
public class ContentIdeasService : IContentIdeasService
{
    internal const string Disclaimer =
        "Gợi ý do AI đề xuất dựa trên mẫu nội dung phổ biến — không phải dữ liệu xu hướng thời gian thực.";

    private static readonly IReadOnlyList<string> DefaultPlatforms =
        new[] { "TikTok", "Instagram Reels", "YouTube Shorts" };

    private const int MaxExistingIdeas = 6;
    private const int MaxExistingIdeaLength = 200;
    private const int MaxNicheLength = 48;
    private const int MinDuration = 10;
    private const int MaxDuration = 180;

    private readonly IContentIdeasAgent _agent;

    public ContentIdeasService(IContentIdeasAgent agent)
    {
        _agent = agent;
    }

    public async Task<ContentIdeaSuggestionsResponse> SuggestAsync(
        ContentIdeaSuggestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var goal = request.Goal;
        var input = new ContentIdeasAgentInput(
            Goal: GoalKeyword(goal),
            RankingPriority: RankingPriority(goal),
            Niche: NormaliseNiche(request.Niche),
            Platforms: NormalisePlatforms(request.Platforms),
            Language: LanguageName(request.Language),
            DurationSeconds: Math.Clamp(request.Duration <= 0 ? 60 : request.Duration, MinDuration, MaxDuration),
            ExistingIdeas: NormaliseExistingIdeas(request.ExistingIdeas));

        var output = await _agent.SuggestAsync(input, cancellationToken);

        var ranked = output.Ideas
            .OrderByDescending(i => RankScore(goal, i))
            .ThenByDescending(i => i.OverallScore)
            .Select((idea, index) => new ContentIdeaSuggestionDto(
                Rank: index + 1,
                Title: idea.Title,
                Niche: idea.Niche,
                Concept: idea.Concept,
                Hook: idea.Hook,
                WhyItWorks: idea.WhyItWorks,
                MonetizationScore: idea.MonetizationScore,
                ViralScore: idea.ViralScore,
                OverallScore: idea.OverallScore))
            .ToList();

        return new ContentIdeaSuggestionsResponse(ranked, GoalKeyword(goal), Disclaimer);
    }

    /// <summary>
    /// Goal-weighted score over the three dimensions the model returns. The
    /// weights mirror the product spec; the model only gives us monetization /
    /// viral / overall, so "audience value" and "scalability" fold into overall.
    /// </summary>
    private static double RankScore(ContentIdeaGoal goal, ContentIdeaItem i) => goal switch
    {
        ContentIdeaGoal.Monetization => i.MonetizationScore * 0.65 + i.OverallScore * 0.35,
        ContentIdeaGoal.Views => i.ViralScore * 0.65 + i.OverallScore * 0.35,
        _ => i.OverallScore * 0.60 + (i.ViralScore + i.MonetizationScore) / 2.0 * 0.40
    };

    private static string GoalKeyword(ContentIdeaGoal goal) => goal switch
    {
        ContentIdeaGoal.Monetization => "monetization",
        ContentIdeaGoal.Views => "views",
        _ => "overall"
    };

    private static string RankingPriority(ContentIdeaGoal goal) => goal switch
    {
        ContentIdeaGoal.Monetization =>
            "monetization potential 40%, audience value 25%, views potential 20%, content scalability 15%",
        ContentIdeaGoal.Views =>
            "viral potential 40%, hook strength 25%, shareability 20%, audience size 15%",
        _ =>
            "views potential 35%, retention/hook 25%, monetization 25%, scalability 15%"
    };

    private static string? NormaliseNiche(string? niche)
    {
        var trimmed = niche?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("discover", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("auto/discover", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed.Length > MaxNicheLength ? trimmed[..MaxNicheLength] : trimmed;
    }

    private static IReadOnlyList<string> NormalisePlatforms(IReadOnlyList<string>? platforms)
    {
        if (platforms is null || platforms.Count == 0)
        {
            return DefaultPlatforms;
        }

        var cleaned = platforms
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Length > 40 ? p.Trim()[..40] : p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return cleaned.Count == 0 ? DefaultPlatforms : cleaned;
    }

    private static IReadOnlyList<string> NormaliseExistingIdeas(IReadOnlyList<string>? ideas)
    {
        if (ideas is null || ideas.Count == 0)
        {
            return Array.Empty<string>();
        }

        return ideas
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .Select(i => i.Length > MaxExistingIdeaLength ? i[..MaxExistingIdeaLength] : i)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxExistingIdeas)
            .ToList();
    }

    /// <summary>
    /// Spelling the language out gives noticeably more reliable model output than
    /// the bare code; unknown codes pass through. Mirrors ScriptAgent.
    /// </summary>
    private static string LanguageName(string? code) => (code ?? "en").Trim().ToLowerInvariant() switch
    {
        "" => "English",
        "vi" or "vi-vn" => "Vietnamese",
        "en" or "en-us" or "en-gb" => "English",
        "ja" => "Japanese",
        "ko" => "Korean",
        "zh" => "Chinese",
        "th" => "Thai",
        "id" => "Indonesian",
        "es" => "Spanish",
        "fr" => "French",
        var other => other
    };
}
