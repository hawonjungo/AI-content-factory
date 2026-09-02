using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiContentFactory.Application.Audio;

/// <summary>
/// Real per-word timestamps from a TTS provider, when it can give them. Gemini's
/// prebuilt-voice path cannot today, so no implementation is registered - but
/// the seam means a provider swap (or a forced-alignment pass) drops in without
/// touching callers.
/// </summary>
public interface IProviderWordTimingSource
{
    /// <summary>Word timings for <paramref name="narrationText"/> as spoken in <paramref name="audioBytes"/>, or null if unavailable.</summary>
    IReadOnlyList<WordTiming>? TryGetWordTimings(string narrationText, byte[] audioBytes, string mimeType);
}

public interface IAudioTimingService
{
    /// <summary>
    /// Produces a word/sentence/segment timing map for a narration clip.
    /// <paramref name="measuredAudioSeconds"/> is the real decoded audio length
    /// (from <see cref="AudioValidator"/>) and is always used as the total;
    /// only the distribution of that total across words is modelled when the
    /// provider gives no per-word data.
    /// </summary>
    AudioTiming Compute(string narrationText, double measuredAudioSeconds, IReadOnlyList<WordTiming>? providerWordTimings = null);

    /// <summary>Serializes a timing for persistence on the scene.</summary>
    string Serialize(AudioTiming timing);

    /// <summary>Reads back a persisted timing; returns <see cref="AudioTiming.Empty"/> for null/blank/corrupt input.</summary>
    AudioTiming Deserialize(string? json);
}

/// <summary>
/// Distributes the measured narration duration across words by a spoken-length
/// weight (letters + a pause allowance after punctuation), so end-of-sentence
/// words hold longer - close to how the line is actually read. When the
/// provider supplies real word timestamps those are used verbatim instead.
/// </summary>
public class AudioTimingService : IAudioTimingService
{
    private static readonly Regex Tokenizer = new(@"\S+", RegexOptions.Compiled);

    /// <summary>Base time weight every word gets, so a one-letter word still occupies a real slot.</summary>
    private const double BaseWeight = 2.0;

    private const double CommaPause = 2.0;   // , ; :  (—)
    private const double PeriodPause = 4.0;  // . ! ? …

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize(AudioTiming timing) => JsonSerializer.Serialize(timing, JsonOptions);

    public AudioTiming Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return AudioTiming.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AudioTiming>(json, JsonOptions) ?? AudioTiming.Empty;
        }
        catch (JsonException)
        {
            return AudioTiming.Empty;
        }
    }

    public AudioTiming Compute(string narrationText, double measuredAudioSeconds, IReadOnlyList<WordTiming>? providerWordTimings = null)
    {
        if (string.IsNullOrWhiteSpace(narrationText) || measuredAudioSeconds <= 0)
        {
            return AudioTiming.Empty;
        }

        var tokens = Tokenizer.Matches(narrationText).Select(m => m.Value).ToArray();
        if (tokens.Length == 0)
        {
            return AudioTiming.Empty;
        }

        WordTiming[] words;
        bool fromProvider;

        if (providerWordTimings is { Count: > 0 })
        {
            words = AlignProviderTimings(tokens, providerWordTimings, measuredAudioSeconds);
            fromProvider = true;
        }
        else
        {
            words = DistributeAcrossDuration(tokens, measuredAudioSeconds);
            fromProvider = false;
        }

        var total = Math.Max(measuredAudioSeconds, words.Length > 0 ? words[^1].EndSeconds : 0);
        var sentences = GroupIntoSentences(words);

        return new AudioTiming(total, words, sentences, sentences, fromProvider);
    }

    private static WordTiming[] DistributeAcrossDuration(string[] tokens, double totalSeconds)
    {
        var weights = new double[tokens.Length];
        double sum = 0;
        for (var i = 0; i < tokens.Length; i++)
        {
            var letters = tokens[i].Count(char.IsLetterOrDigit);
            weights[i] = BaseWeight + Math.Max(1, letters) + PunctuationPause(tokens[i]);
            sum += weights[i];
        }

        var result = new WordTiming[tokens.Length];
        var cursor = 0.0;
        for (var i = 0; i < tokens.Length; i++)
        {
            var end = i == tokens.Length - 1
                ? totalSeconds
                : cursor + totalSeconds * (weights[i] / sum);
            if (end <= cursor)
            {
                end = Math.Min(totalSeconds, cursor + 0.01);
            }

            result[i] = new WordTiming(tokens[i], cursor, end);
            cursor = end;
        }

        return result;
    }

    private static WordTiming[] AlignProviderTimings(string[] tokens, IReadOnlyList<WordTiming> provider, double totalSeconds)
    {
        // Trust the provider's times but pin the count to our tokenization so
        // sentence grouping and caption mapping stay consistent.
        if (provider.Count == tokens.Length)
        {
            return tokens.Select((t, i) => new WordTiming(t, provider[i].StartSeconds, provider[i].EndSeconds)).ToArray();
        }

        // Count mismatch: fall back to proportional placement using the
        // provider's overall span so timing still tracks the audio.
        var span = Math.Max(totalSeconds, provider[^1].EndSeconds);
        return DistributeAcrossDuration(tokens, span);
    }

    private static double PunctuationPause(string token)
    {
        var last = token.Length == 0 ? '\0' : token[^1];
        return last switch
        {
            '.' or '!' or '?' or '…' => PeriodPause,
            ',' or ';' or ':' or '—' or '–' => CommaPause,
            _ => 0
        };
    }

    private static List<SentenceTiming> GroupIntoSentences(WordTiming[] words)
    {
        var sentences = new List<SentenceTiming>();
        var current = new List<WordTiming>();

        foreach (var word in words)
        {
            current.Add(word);
            var last = word.Text.Length == 0 ? '\0' : word.Text[^1];
            if (last is '.' or '!' or '?' or '…')
            {
                sentences.Add(BuildSentence(current));
                current = new List<WordTiming>();
            }
        }

        if (current.Count > 0)
        {
            sentences.Add(BuildSentence(current));
        }

        return sentences;
    }

    private static SentenceTiming BuildSentence(List<WordTiming> words) => new(
        string.Join(" ", words.Select(w => w.Text)),
        words[0].StartSeconds,
        words[^1].EndSeconds,
        words.ToArray());
}
