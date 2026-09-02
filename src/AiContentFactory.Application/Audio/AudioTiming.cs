namespace AiContentFactory.Application.Audio;

/// <param name="Text">The word as spoken, punctuation attached.</param>
public record WordTiming(string Text, double StartSeconds, double EndSeconds)
{
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

public record SentenceTiming(string Text, double StartSeconds, double EndSeconds, IReadOnlyList<WordTiming> Words)
{
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
}

/// <param name="Segments">
/// Larger-than-sentence spans (currently the same as <see cref="Sentences"/>);
/// kept as a distinct list so a future provider that returns paragraph or
/// phrase boundaries can populate it without changing the contract.
/// </param>
/// <param name="FromProviderTimestamps">
/// True when word times came from the TTS provider itself; false when they were
/// distributed across the (accurate, measured) total duration by this service.
/// Either way <see cref="TotalSeconds"/> is the real measured audio length -
/// never an estimate from the storyboard plan.
/// </param>
public record AudioTiming(
    double TotalSeconds,
    IReadOnlyList<WordTiming> Words,
    IReadOnlyList<SentenceTiming> Sentences,
    IReadOnlyList<SentenceTiming> Segments,
    bool FromProviderTimestamps)
{
    public static readonly AudioTiming Empty =
        new(0, Array.Empty<WordTiming>(), Array.Empty<SentenceTiming>(), Array.Empty<SentenceTiming>(), false);

    public bool HasTiming => TotalSeconds > 0 && Words.Count > 0;
}
