using System.Globalization;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Rendering;

/// <param name="AtSeconds">When the effect fires on the finished timeline.</param>
public record SfxCue(string AbsolutePath, double AtSeconds, double Volume = 0.8);

public record AudioMixRequest(
    string? MusicPath,
    IReadOnlyList<SfxCue>? Sfx,
    string? AmbiencePath,
    double TotalSeconds);

/// <summary>The resolved mix: which tracks, at what levels, with what ducking and fades.</summary>
public record AudioMixSpec(
    string? MusicPath,
    double MusicVolume,
    double DuckThreshold,
    double DuckRatio,
    int DuckAttackMs,
    int DuckReleaseMs,
    IReadOnlyList<SfxCue> Sfx,
    string? AmbiencePath,
    double AmbienceVolume,
    double FadeInSeconds,
    double FadeOutSeconds,
    double TotalSeconds)
{
    /// <summary>True when there is anything to mix on top of the bare narration.</summary>
    public bool HasBed => MusicPath is not null || AmbiencePath is not null || Sfx.Count > 0;
}

/// <param name="NarrationInput">ffmpeg input index carrying the concatenated narration audio (usually the composited video, input 0).</param>
public record AudioInputLayout(
    int NarrationInput,
    int? MusicInput,
    int? AmbienceInput,
    IReadOnlyList<int> SfxInputs);

public record AudioMixGraph(string FilterComplex, string OutLabel);

public class AudioMixOptions
{
    public const string SectionName = "Audio:Mix";

    public double MusicVolume { get; set; } = 0.22;
    public double AmbienceVolume { get; set; } = 0.12;

    /// <summary>sidechaincompress params - music is pushed down while narration is present.</summary>
    public double DuckThreshold { get; set; } = 0.03;
    public double DuckRatio { get; set; } = 8;
    public int DuckAttackMs { get; set; } = 20;
    public int DuckReleaseMs { get; set; } = 350;

    public double FadeInSeconds { get; set; } = 0.4;
    public double FadeOutSeconds { get; set; } = 0.8;
}

public interface IAudioMixingService
{
    /// <summary>Resolves a mix request against configured defaults.</summary>
    AudioMixSpec BuildSpec(AudioMixRequest request);

    /// <summary>
    /// Builds the ffmpeg <c>-filter_complex</c> for the mix: narration is the
    /// bed, music ducks under it via sidechain compression, ambience sits low,
    /// SFX are delayed to their cue, and the whole mix fades in and out - no
    /// abrupt cuts.
    /// </summary>
    AudioMixGraph BuildFilterGraph(AudioMixSpec spec, AudioInputLayout inputs);
}

public class AudioMixingService : IAudioMixingService
{
    private readonly AudioMixOptions _options;

    public AudioMixingService(IOptions<AudioMixOptions> options)
    {
        _options = options.Value;
    }

    public AudioMixSpec BuildSpec(AudioMixRequest request) => new(
        MusicPath: string.IsNullOrWhiteSpace(request.MusicPath) ? null : request.MusicPath,
        MusicVolume: _options.MusicVolume,
        DuckThreshold: _options.DuckThreshold,
        DuckRatio: _options.DuckRatio,
        DuckAttackMs: _options.DuckAttackMs,
        DuckReleaseMs: _options.DuckReleaseMs,
        Sfx: request.Sfx?.Where(s => !string.IsNullOrWhiteSpace(s.AbsolutePath)).ToList() ?? new List<SfxCue>(),
        AmbiencePath: string.IsNullOrWhiteSpace(request.AmbiencePath) ? null : request.AmbiencePath,
        AmbienceVolume: _options.AmbienceVolume,
        FadeInSeconds: _options.FadeInSeconds,
        FadeOutSeconds: _options.FadeOutSeconds,
        TotalSeconds: Math.Max(0, request.TotalSeconds));

    public AudioMixGraph BuildFilterGraph(AudioMixSpec spec, AudioInputLayout inputs)
    {
        var steps = new List<string>();
        var fadeOutStart = Math.Max(0, spec.TotalSeconds - spec.FadeOutSeconds);

        // Narration bed.
        steps.Add($"[{inputs.NarrationInput}:a]aresample=async=1:first_pts=0[narrbed]");
        var running = "narrbed";

        // Music, ducked under the narration. The narration bed feeds both the
        // final mix and the sidechain key, so it is split first - a filtergraph
        // label can only be consumed once.
        if (spec.MusicPath is not null && inputs.MusicInput is { } mi)
        {
            steps.Add($"[{running}]asplit=2[narrmix][narrkey]");
            steps.Add(
                $"[{mi}:a]volume={F(spec.MusicVolume)}," +
                $"afade=t=in:st=0:d={F(spec.FadeInSeconds)}," +
                $"afade=t=out:st={F(fadeOutStart)}:d={F(spec.FadeOutSeconds)}[music0]");
            steps.Add(
                $"[music0][narrkey]sidechaincompress=threshold={F(spec.DuckThreshold)}:" +
                $"ratio={F(spec.DuckRatio)}:attack={spec.DuckAttackMs}:release={spec.DuckReleaseMs}[musicduck]");
            steps.Add($"[narrmix][musicduck]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[mixm]");
            running = "mixm";
        }

        // Ambience, low and constant.
        if (spec.AmbiencePath is not null && inputs.AmbienceInput is { } ai)
        {
            steps.Add(
                $"[{ai}:a]volume={F(spec.AmbienceVolume)}," +
                $"afade=t=in:st=0:d={F(spec.FadeInSeconds)}," +
                $"afade=t=out:st={F(fadeOutStart)}:d={F(spec.FadeOutSeconds)}[amb0]");
            steps.Add($"[{running}][amb0]amix=inputs=2:duration=first:normalize=0[mixa]");
            running = "mixa";
        }

        // SFX, each delayed to its cue.
        if (spec.Sfx.Count > 0 && inputs.SfxInputs.Count == spec.Sfx.Count)
        {
            var labels = new List<string>();
            for (var i = 0; i < spec.Sfx.Count; i++)
            {
                var delayMs = (int)Math.Round(Math.Max(0, spec.Sfx[i].AtSeconds) * 1000);
                steps.Add($"[{inputs.SfxInputs[i]}:a]adelay={delayMs}|{delayMs},volume={F(spec.Sfx[i].Volume)}[sfx{i}]");
                labels.Add($"[sfx{i}]");
            }

            steps.Add($"[{running}]{string.Concat(labels)}amix=inputs={1 + spec.Sfx.Count}:duration=first:normalize=0[mixs]");
            running = "mixs";
        }

        // Whole-mix fade out so nothing ends on a hard cut.
        steps.Add($"[{running}]afade=t=out:st={F(fadeOutStart)}:d={F(spec.FadeOutSeconds)},aresample=async=1[aout]");

        return new AudioMixGraph(string.Join(";", steps), "[aout]");
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
