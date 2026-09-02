using System.Text.RegularExpressions;
using AiContentFactory.Application.Audio;
using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.Rendering;

/// <param name="CaptionText">The on-screen text for the scene (Scene.EffectiveCaptionText) - distinct from the narration.</param>
/// <param name="Timing">Word/sentence timing from <see cref="IAudioTimingService"/> for the scene's narration.</param>
/// <param name="SceneOffsetSeconds">Where this scene starts on the finished timeline.</param>
public record SceneCaptionInput(string CaptionText, AudioTiming Timing, double SceneOffsetSeconds);

public interface ICaptionSegmentationService
{
    /// <summary>
    /// Splits each scene's caption text into short, readable cues aligned to
    /// the narration timing. Never uses <c>string.Split('.')</c>: punctuation
    /// only nudges a break that word-count and readability rules decide.
    /// </summary>
    IReadOnlyList<CaptionCue> Segment(
        IReadOnlyList<SceneCaptionInput> scenes,
        CaptionSettings settings,
        CaptionSegmentationOptions options);
}

public class CaptionSegmentationService : ICaptionSegmentationService
{
    private static readonly Regex Tokenizer = new(@"\S+", RegexOptions.Compiled);
    private const double MinCueSeconds = 0.30;

    public IReadOnlyList<CaptionCue> Segment(
        IReadOnlyList<SceneCaptionInput> scenes,
        CaptionSettings settings,
        CaptionSegmentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        var effectiveMax = Clamp(
            settings.MaxWordsPerCue > 0 ? Math.Min(options.MaxWords, settings.MaxWordsPerCue) : options.MaxWords,
            options.MinWords,
            Math.Max(options.MaxWords, options.MinWords));
        var hardMax = Math.Max(effectiveMax, options.HardMaxWords);
        var target = Clamp(options.TargetWords, options.MinWords, effectiveMax);

        var cues = new List<CaptionCue>();

        foreach (var scene in scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.CaptionText) || !scene.Timing.HasTiming)
            {
                continue;
            }

            var words = MapWords(scene);
            if (words.Count == 0)
            {
                continue;
            }

            var groups = GroupWords(words, target, effectiveMax, hardMax, options);
            foreach (var group in groups)
            {
                cues.Add(BuildCue(group, scene.SceneOffsetSeconds));
            }
        }

        return EnforceMonotonic(cues);
    }

    /// <summary>Rough line count for a rendered cue - used by tests and the final validator, not by libass.</summary>
    public static int EstimateLineCount(string text, int maxCharsPerLine)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var lines = 1;
        var lineLen = 0;
        foreach (var word in Tokenizer.Matches(text).Select(m => m.Value))
        {
            var add = (lineLen == 0 ? 0 : 1) + word.Length;
            if (lineLen > 0 && lineLen + add > maxCharsPerLine)
            {
                lines++;
                lineLen = word.Length;
            }
            else
            {
                lineLen += add;
            }
        }

        return lines;
    }

    /// <summary>
    /// Scene-local word timings for the caption text. When the caption text is
    /// the narration verbatim (the common case) the narration word times are
    /// used directly; otherwise the narration span is divided across the caption
    /// words by spoken-length weight so the captions still track the audio.
    /// </summary>
    private static List<CaptionWord> MapWords(SceneCaptionInput scene)
    {
        var tokens = Tokenizer.Matches(scene.CaptionText).Select(m => m.Value).ToArray();
        if (tokens.Length == 0)
        {
            return new List<CaptionWord>();
        }

        var narration = scene.Timing.Words;
        if (narration.Count == tokens.Length)
        {
            return tokens
                .Select((t, i) => new CaptionWord(t, narration[i].StartSeconds, narration[i].EndSeconds))
                .ToList();
        }

        var span = scene.Timing.TotalSeconds;
        var weights = tokens.Select(t => 2.0 + Math.Max(1, t.Count(char.IsLetterOrDigit))).ToArray();
        var sum = weights.Sum();

        var result = new List<CaptionWord>(tokens.Length);
        var cursor = 0.0;
        for (var i = 0; i < tokens.Length; i++)
        {
            var end = i == tokens.Length - 1 ? span : cursor + span * (weights[i] / sum);
            if (end <= cursor)
            {
                end = Math.Min(span, cursor + 0.01);
            }

            result.Add(new CaptionWord(tokens[i], cursor, end));
            cursor = end;
        }

        return result;
    }

    private static List<List<CaptionWord>> GroupWords(
        List<CaptionWord> words,
        int target,
        int effectiveMax,
        int hardMax,
        CaptionSegmentationOptions options)
    {
        var groups = new List<List<CaptionWord>>();
        var current = new List<CaptionWord>();
        var currentChars = 0;

        foreach (var word in words)
        {
            var addChars = (current.Count == 0 ? 0 : 1) + word.Text.Length;

            var wouldOverflowChars = current.Count >= options.MinWords && currentChars + addChars > options.MaxCharsPerCue;
            var atMax = current.Count >= effectiveMax;

            if (current.Count > 0 && (atMax || wouldOverflowChars))
            {
                groups.Add(current);
                current = new List<CaptionWord>();
                currentChars = 0;
                addChars = word.Text.Length;
            }

            current.Add(word);
            currentChars += addChars;

            // Punctuation *guides*: once the cue is a sensible length and the
            // word ends a clause/sentence, break here rather than mid-phrase.
            var endsClause = EndsClause(word.Text);
            if (endsClause && current.Count >= Math.Min(target, options.MinWords + 1) && current.Count >= options.MinWords)
            {
                groups.Add(current);
                current = new List<CaptionWord>();
                currentChars = 0;
            }
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return Rebalance(groups, options.MinWords, hardMax, options.MaxCharsPerCue);
    }

    /// <summary>
    /// Removes 1-2 word cues: merge a short tail into its neighbour when that
    /// still fits, otherwise shift a single word across so neither side is tiny.
    /// </summary>
    private static List<List<CaptionWord>> Rebalance(List<List<CaptionWord>> groups, int minWords, int hardMax, int maxChars)
    {
        var changed = true;
        while (changed)
        {
            changed = false;

            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Count >= minWords || groups.Count == 1)
                {
                    continue;
                }

                var neighbourIndex = i > 0 ? i - 1 : i + 1;
                if (neighbourIndex < 0 || neighbourIndex >= groups.Count)
                {
                    break;
                }

                var merged = neighbourIndex < i
                    ? groups[neighbourIndex].Concat(groups[i]).ToList()
                    : groups[i].Concat(groups[neighbourIndex]).ToList();

                if (merged.Count <= hardMax && CharCount(merged) <= maxChars)
                {
                    groups[Math.Min(i, neighbourIndex)] = merged;
                    groups.RemoveAt(Math.Max(i, neighbourIndex));
                    changed = true;
                    break;
                }

                // Can't merge: pull one word from the bigger neighbour instead.
                if (i > 0 && groups[i - 1].Count > minWords)
                {
                    var moved = groups[i - 1][^1];
                    groups[i - 1].RemoveAt(groups[i - 1].Count - 1);
                    groups[i].Insert(0, moved);
                    changed = true;
                    break;
                }

                if (i + 1 < groups.Count && groups[i + 1].Count > minWords)
                {
                    var moved = groups[i + 1][0];
                    groups[i + 1].RemoveAt(0);
                    groups[i].Add(moved);
                    changed = true;
                    break;
                }
            }
        }

        return groups;
    }

    private static CaptionCue BuildCue(IReadOnlyList<CaptionWord> group, double offset)
    {
        var start = offset + group[0].StartSeconds;
        var end = offset + group[^1].EndSeconds;
        if (end - start < MinCueSeconds)
        {
            end = start + MinCueSeconds;
        }

        var shifted = group
            .Select(w => new CaptionWord(w.Text, offset + w.StartSeconds, offset + w.EndSeconds))
            .ToList();

        return new CaptionCue(start, end, shifted);
    }

    private static IReadOnlyList<CaptionCue> EnforceMonotonic(List<CaptionCue> cues)
    {
        var ordered = cues.OrderBy(c => c.StartSeconds).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].StartSeconds < ordered[i - 1].EndSeconds)
            {
                var start = ordered[i - 1].EndSeconds;
                var end = Math.Max(start + MinCueSeconds, ordered[i].EndSeconds);
                ordered[i] = ordered[i] with { StartSeconds = start, EndSeconds = end };
            }
        }

        return ordered;
    }

    private static bool EndsClause(string token)
    {
        var last = token.Length == 0 ? '\0' : token[^1];
        return last is '.' or '!' or '?' or '…' or ',' or ';' or ':' or '—' or '–';
    }

    private static int CharCount(IReadOnlyList<CaptionWord> words) =>
        words.Sum(w => w.Text.Length) + Math.Max(0, words.Count - 1);

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
