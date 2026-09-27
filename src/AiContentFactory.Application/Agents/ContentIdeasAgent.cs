using AiContentFactory.Application.Providers;

namespace AiContentFactory.Application.Agents;

/// <param name="Goal">Ranking objective: "monetization", "views" or "overall".</param>
/// <param name="RankingPriority">Human-readable weighting for the goal - fed to the model so its own ordering roughly matches the server re-rank.</param>
/// <param name="Niche">A specific niche name, or null to let the model discover the best-fitting niches itself.</param>
/// <param name="Platforms">Target short-form platforms (already sanitised).</param>
/// <param name="Language">Spelled-out language for titles/hooks/concepts ("English", "Vietnamese", ...).</param>
/// <param name="DurationSeconds">Target finished-video length - keeps concepts realistic for the pipeline.</param>
/// <param name="ExistingIdeas">Ideas the user already has (or previous suggestions to avoid repeating). May be empty.</param>
public record ContentIdeasAgentInput(
    string Goal,
    string RankingPriority,
    string? Niche,
    IReadOnlyList<string> Platforms,
    string Language,
    int DurationSeconds,
    IReadOnlyList<string> ExistingIdeas);

/// <summary>One idea exactly as the model returned it - scores are the model's, ranking is re-derived server-side.</summary>
public record ContentIdeaItem(
    int Rank,
    string Title,
    string Niche,
    string Concept,
    string Hook,
    string WhyItWorks,
    int MonetizationScore,
    int ViralScore,
    int OverallScore);

public record ContentIdeasAgentOutput(IReadOnlyList<ContentIdeaItem> Ideas);

public interface IContentIdeasAgent
{
    Task<ContentIdeasAgentOutput> SuggestAsync(ContentIdeasAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Step 2 helper: asks the cheap text model to select the single
/// highest-potential short-form video idea at IDEA level only - title, niche,
/// one-line concept, an opening hook line, a reason, and three 0-100 scores.
/// Deliberately does NOT produce scripts, storyboards, captions or prompts;
/// those stay the responsibility of the later pipeline steps. The model does
/// the shortlisting internally but must return only its winner (was 3, and
/// originally 10) - this keeps per-call token cost minimal and lets the
/// frontend auto-apply the result without a separate "pick one" step.
/// </summary>
public class ContentIdeasAgent : IContentIdeasAgent
{
    private const int IdeaCount = 1;

    private const string SystemPrompt = """
        You are a content strategist for faceless 9:16 short-form videos on TikTok, Instagram Reels and YouTube Shorts.
        Do not brainstorm multiple ideas and return several - select and return ONLY the single strongest idea for the given niche and goal.
        Respond with ONLY one JSON object, no markdown fences, matching exactly this schema:
        {"ideas":[{"rank":number,"title":string,"niche":string,"concept":string,"hook":string,"why_it_works":string,"monetization_score":number,"viral_score":number,"overall_score":number}]}
        Rules:
        - Return EXACTLY 1 idea - your single best one, not a shortlist trimmed down. rank is always 1.
        - Pick by, in priority order (weighted by the stated ranking priority): (1) overall potential, (2) hook/retention potential, (3) viral potential, (4) monetization potential, (5) how realistic it is to produce with AI narration + AI images/short AI video clips.
        - The idea should have a strong first-second hook, a clear story/concept, easy visual execution, good retention potential, and ideally work as a repeatable series rather than a one-off.
        - All three scores are integers 0-100.
        - title: usable as-is as a short-form video title.
        - concept: 1-2 sentences describing the video, realistic to produce as narration + AI images/short AI video clips.
        - hook: a single strong spoken opening line (the first thing the viewer hears).
        - why_it_works: one short sentence on why the idea is worth making.
        - Do NOT write full scripts, scene lists, shot lists, captions or voice-over.
        - These are AI recommendations inferred from general content patterns, NOT real-time trend data - never claim an idea is "currently trending".
        - No text of any kind outside the JSON object.
        """;

    private readonly ILlmRouter _router;

    public ContentIdeasAgent(ILlmRouter router)
    {
        _router = router;
    }

    /// <summary>Loose shape for the first parse pass - validation/normalisation happens after.</summary>
    private record RawIdea(
        int? Rank,
        string? Title,
        string? Niche,
        string? Concept,
        string? Hook,
        string? Why_It_Works,
        double? Monetization_Score,
        double? Viral_Score,
        double? Overall_Score);

    private record RawOutput(IReadOnlyList<RawIdea>? Ideas);

    public async Task<ContentIdeasAgentOutput> SuggestAsync(ContentIdeasAgentInput input, CancellationToken cancellationToken = default)
    {
        var existingBlock = input.ExistingIdeas.Count == 0
            ? "(none - the user has no ideas yet, research the opportunity space yourself)"
            : "Do NOT repeat, rephrase or lightly reword any of these - propose genuinely different angles:\n" +
              string.Join("\n", input.ExistingIdeas.Select(i => $"- {i}"));

        var userPrompt = $"""
            Goal: {input.Goal}
            Ranking priority: {input.RankingPriority}
            Niche: {(string.IsNullOrWhiteSpace(input.Niche) ? "discover the best-fitting niches for this goal yourself" : input.Niche)}
            Platforms: {string.Join(", ", input.Platforms)}
            Language for title / hook / concept / why: {input.Language}
            Target finished video length: {input.DurationSeconds} seconds

            Existing ideas:
            {existingBlock}

            Return only your single best idea now.
            """;

        var raw = await JsonAgentRunner.RunAsync<RawOutput>(
            _router.Resolve(LlmTaskType.Ideas),
            SystemPrompt,
            userPrompt,
            validate: IsUsable,
            cancellationToken);

        // Past validation the list is non-null, count IdeaCount, every field
        // present and every score in range - so this mapping is safe.
        var ideas = raw.Ideas!
            .Select((r, index) => new ContentIdeaItem(
                Rank: r.Rank is >= 1 and <= IdeaCount ? r.Rank.Value : index + 1,
                Title: r.Title!.Trim(),
                Niche: r.Niche!.Trim(),
                Concept: r.Concept!.Trim(),
                Hook: r.Hook!.Trim(),
                WhyItWorks: r.Why_It_Works!.Trim(),
                MonetizationScore: Clamp(r.Monetization_Score!.Value),
                ViralScore: Clamp(r.Viral_Score!.Value),
                OverallScore: Clamp(r.Overall_Score!.Value)))
            .ToList();

        return new ContentIdeasAgentOutput(ideas);
    }

    private static bool IsUsable(RawOutput output)
    {
        if (output.Ideas is not { Count: IdeaCount })
        {
            return false;
        }

        return output.Ideas.All(i =>
            !string.IsNullOrWhiteSpace(i.Title) &&
            !string.IsNullOrWhiteSpace(i.Niche) &&
            !string.IsNullOrWhiteSpace(i.Concept) &&
            !string.IsNullOrWhiteSpace(i.Hook) &&
            !string.IsNullOrWhiteSpace(i.Why_It_Works) &&
            InRange(i.Monetization_Score) &&
            InRange(i.Viral_Score) &&
            InRange(i.Overall_Score));
    }

    private static bool InRange(double? score) => score is >= 0 and <= 100;

    private static int Clamp(double score) => (int)Math.Round(Math.Clamp(score, 0, 100));
}
