using System.Globalization;
using System.Text;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Infrastructure.Rendering;

/// <summary>
/// Writes ASS (Advanced SubStation Alpha) subtitles for libass, which is what
/// ffmpeg's `subtitles` filter uses.
///
/// This replaced SRT + a hardcoded force_style string. SRT can express only
/// "text between two timestamps"; everything a caption preset needs - font,
/// colour, outline, on-screen position, word grouping, per-word highlighting,
/// entry animation - has no representation in it at all.
///
/// Karaoke note: rather than ASS's own \k tags (which colour every word
/// already spoken, so the whole line gradually changes), each word gets its
/// own Dialogue line showing the full cue with just that word in the highlight
/// colour. The layout is identical across those lines, so nothing shifts on
/// screen, and only the current word is highlighted - the effect people
/// actually mean by "TikTok captions".
/// </summary>
public static class AssSubtitleWriter
{
    public static void Write(IReadOnlyList<CaptionCue> cues, CaptionSettings settings, int width, int height, string outputPath)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, settings, width, height);

        foreach (var cue in cues)
        {
            if (settings.Karaoke && cue.Words.Count > 1)
            {
                AppendKaraokeCue(sb, cue, settings, width, height);
            }
            else
            {
                AppendPlainCue(sb, cue, settings, width, height);
            }
        }

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Single centred cue used by the caption preview - no timeline, just "what does this look like".</summary>
    public static void WriteSingleCue(string text, CaptionSettings settings, int width, int height, double durationSeconds, string outputPath)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Take(Math.Max(1, settings.MaxWordsPerCue))
            .ToList();

        if (words.Count == 0)
        {
            words.Add("Preview");
        }

        var step = durationSeconds / words.Count;
        var timed = words
            .Select((w, i) => new CaptionWord(w, i * step, (i + 1) * step))
            .ToList();

        Write(new[] { new CaptionCue(0, durationSeconds, timed) }, settings, width, height, outputPath);
    }

    private static void AppendHeader(StringBuilder sb, CaptionSettings settings, int width, int height)
    {
        // ScaledBorderAndShadow keeps outline thickness proportional if libass
        // ever renders at a size other than PlayResX/Y.
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine(FormattableString.Invariant($"PlayResX: {width}"));
        sb.AppendLine(FormattableString.Invariant($"PlayResY: {height}"));
        sb.AppendLine("WrapStyle: 2");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine("YCbCr Matrix: TV.709");
        sb.AppendLine();

        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");

        var horizontalMargin = Math.Max(40, width / 12);

        sb.AppendLine(string.Join(",", new[]
        {
            "Style: Default",
            settings.FontFamily,
            Num(settings.FontSizePt),
            ToAssColor(settings.PrimaryColor),
            ToAssColor(settings.HighlightColor),
            ToAssColor(settings.OutlineColor),
            ToAssColor("#000000"),
            settings.Bold ? "-1" : "0",   // ASS booleans are -1/0, not 1/0
            "0",
            "0",
            "0",
            "100",
            "100",
            "0",
            "0",
            "1",                          // BorderStyle 1 = outline + drop shadow
            Num(settings.OutlineWidth),
            Num(settings.ShadowDepth),
            Alignment(settings.Position),
            Num(horizontalMargin),
            Num(horizontalMargin),
            Num(settings.Position == CaptionPosition.Center ? 0 : settings.MarginVerticalPx),
            "1"
        }));

        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
    }

    private static void AppendPlainCue(StringBuilder sb, CaptionCue cue, CaptionSettings settings, int width, int height)
    {
        var text = Prepare(cue.Text, settings);
        AppendDialogue(sb, cue.StartSeconds, cue.EndSeconds, EntryTags(settings, width, height) + text);
    }

    private static void AppendKaraokeCue(StringBuilder sb, CaptionCue cue, CaptionSettings settings, int width, int height)
    {
        var primary = ToAssColor(settings.PrimaryColor);
        var highlight = ToAssColor(settings.HighlightColor);

        for (var active = 0; active < cue.Words.Count; active++)
        {
            var word = cue.Words[active];

            var line = new StringBuilder();

            // The entry animation belongs to the cue, not to each word - only
            // the first slice replays it, otherwise the line would re-pop on
            // every single word.
            line.Append(active == 0 ? EntryTags(settings, width, height) : PositionTags(settings, width, height));

            for (var i = 0; i < cue.Words.Count; i++)
            {
                if (i > 0)
                {
                    line.Append(' ');
                }

                line.Append("{\\c").Append(i == active ? highlight : primary).Append('}');
                line.Append(Prepare(cue.Words[i].Text, settings));
            }

            // The final word runs to the cue's end so the cue doesn't blink out
            // early if its last word's estimated span finished sooner.
            var end = active == cue.Words.Count - 1 ? cue.EndSeconds : word.EndSeconds;
            AppendDialogue(sb, word.StartSeconds, end, line.ToString());
        }
    }

    /// <summary>Position plus entry animation, applied when a cue first appears.</summary>
    private static string EntryTags(CaptionSettings settings, int width, int height) => settings.Animation switch
    {
        CaptionAnimation.FadeIn => PositionTags(settings, width, height) + "{\\fad(140,90)}",
        CaptionAnimation.PopIn => PositionTags(settings, width, height) + "{\\fscx82\\fscy82\\t(0,110,\\fscx100\\fscy100)}",
        CaptionAnimation.SlideUp => SlideUpTags(settings, width, height),
        _ => PositionTags(settings, width, height)
    };

    /// <summary>
    /// Style alignment/margins already place the text, so nothing is emitted
    /// for the common case - except for SlideUp, which needs \move and
    /// therefore explicit coordinates.
    /// </summary>
    private static string PositionTags(CaptionSettings settings, int width, int height) =>
        settings.Animation == CaptionAnimation.SlideUp
            ? FormattableString.Invariant($"{{\\an{Alignment(settings.Position)}\\pos({width / 2},{AnchorY(settings, height)})}}")
            : string.Empty;

    private static string SlideUpTags(CaptionSettings settings, int width, int height)
    {
        var y = AnchorY(settings, height);
        var from = y + Math.Max(24, settings.FontSizePt / 2);

        return FormattableString.Invariant(
            $"{{\\an{Alignment(settings.Position)}\\move({width / 2},{from},{width / 2},{y},0,160)\\fad(120,60)}}");
    }

    private static int AnchorY(CaptionSettings settings, int height) => settings.Position switch
    {
        CaptionPosition.Top => settings.MarginVerticalPx,
        CaptionPosition.Center => height / 2,
        _ => height - settings.MarginVerticalPx
    };

    private static void AppendDialogue(StringBuilder sb, double start, double end, string text)
    {
        // A zero-or-negative span would be dropped by libass without warning.
        var safeEnd = end <= start ? start + 0.05 : end;
        sb.AppendLine($"Dialogue: 0,{Timestamp(start)},{Timestamp(safeEnd)},Default,,0,0,0,,{text}");
    }

    private static string Prepare(string text, CaptionSettings settings)
    {
        var value = settings.Uppercase ? text.ToUpperInvariant() : text;

        // Braces open override blocks and backslashes start escapes, so any of
        // either in narration would corrupt the line rather than display.
        return value
            .Replace("\\", "/")
            .Replace("{", "(")
            .Replace("}", ")")
            .Replace("\r", string.Empty)
            .Replace("\n", " ");
    }

    /// <summary>ASS numeric alignment: 8 = top centre, 5 = middle centre, 2 = bottom centre.</summary>
    private static string Alignment(CaptionPosition position) => position switch
    {
        CaptionPosition.Top => "8",
        CaptionPosition.Center => "5",
        _ => "2"
    };

    /// <summary>ASS colours are &amp;HAABBGGRR - alpha first, then BGR, the reverse of CSS hex.</summary>
    private static string ToAssColor(string hex)
    {
        var value = hex.TrimStart('#');
        var r = value[..2];
        var g = value.Substring(2, 2);
        var b = value.Substring(4, 2);
        return $"&H00{b}{g}{r}".ToUpperInvariant();
    }

    private static string Timestamp(double totalSeconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        // ASS uses a single hour digit and centiseconds, not SRT's HH:MM:SS,mmm.
        return FormattableString.Invariant($"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}");
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
