using System.Diagnostics;
using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Real ffmpeg/ffprobe composition of a one-scene video, end to end. Runs only
/// where the binaries are on PATH (CI / the API Docker image); it no-ops on a
/// dev box without ffmpeg rather than failing the suite.
///
/// It proves the pieces fit together: a still animates, generated narration is
/// muxed and survives to the output audio stream, the file is a readable 9:16
/// MP4 of the expected length, and the quality validator passes it.
/// </summary>
public class EndToEndCompositionSmokeTests
{
    [Fact]
    public async Task Composes_a_playable_9x16_mp4_with_audio_from_a_still_and_narration()
    {
        var haveTools = ToolOnPath("ffmpeg") && ToolOnPath("ffprobe");
        if (!haveTools)
        {
            // Opt-in hard failure so CI / the container can insist the smoke actually ran.
            Assert.False(Environment.GetEnvironmentVariable("REQUIRE_FFMPEG") == "1",
                "REQUIRE_FFMPEG=1 but ffmpeg/ffprobe were not detected on PATH");
            return;
        }

        var work = Directory.CreateTempSubdirectory("e2e-comp-");
        try
        {
            var still = Path.Combine(work.FullName, "still.png");
            var voice = Path.Combine(work.FullName, "voice.wav");
            var output = Path.Combine(work.FullName, "final.mp4");

            await Run("ffmpeg", $"-y -loglevel error -f lavfi -i color=c=blue:s=1080x1920 -frames:v 1 \"{still}\"");
            await Run("ffmpeg", $"-y -loglevel error -f lavfi -i sine=frequency=220:duration=4 -ar 24000 -ac 1 \"{voice}\"");

            // The audio must pass validation before we ever compose with it.
            var validation = new AudioValidator().Validate(await File.ReadAllBytesAsync(voice), "audio/wav");
            Assert.True(validation.IsValid, validation.Error);
            Assert.False(validation.IsSilent);

            var mixing = new AudioMixingService(Options.Create(new AudioMixOptions()));
            var renderer = new FfmpegVideoRenderer(mixing, NullLogger<FfmpegVideoRenderer>.Instance);
            var probe = new FfprobeMediaProbe(NullLogger<FfprobeMediaProbe>.Instance);
            var validator = new VideoQualityValidator(probe);

            var scene = new RenderScene(still, voice, validation.DurationSeconds, Narration: "", IsStillImage: true, Motion: SceneMotion.KenBurnsIn);
            var mix = mixing.BuildSpec(new AudioMixRequest(null, null, null, validation.DurationSeconds));
            var captionsOff = DisabledCaptions();

            var render = await renderer.RenderAsync(new RenderRequest(
                new[] { scene }, null, output, captionsOff, Array.Empty<CaptionCue>(),
                Width: 1080, Height: 1920, Fps: 30, AudioMix: mix));

            var media = await probe.ProbeAsync(output);
            Assert.True(media.Ok, media.Error);
            Assert.True(media.HasVideo);
            Assert.True(media.HasAudio);                       // narration survived to the final mix
            Assert.Equal(1080, media.Width);
            Assert.Equal(1920, media.Height);                  // 9:16
            Assert.InRange(media.DurationSeconds, 3.0, 6.0);

            var report = await validator.ValidateAsync(new VideoValidationContext(
                output, 1080, 1920, 30,
                ExpectedMinSeconds: 3.0, ExpectedMaxSeconds: 6.0,
                NarrationSeconds: validation.DurationSeconds,
                CaptionsEnabled: false,
                Cues: Array.Empty<CaptionCue>(),
                SceneCount: 1, ScenesWithVisual: 1,
                RenderSucceeded: true));

            Assert.True(report.IsValid, report.Summary);
            Assert.True(render.DurationSeconds > 0);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// The full Section 8 workflow, composition half: ~60s, 9:16, 2 AI-video
    /// scenes + 3 image scenes with motion, a male-voiced and a female-voiced
    /// narration segment, burned captions, a music bed and one SFX, fade
    /// transitions - then the real quality validator.
    /// </summary>
    [Fact]
    public async Task Composes_a_full_multi_scene_60s_video_with_music_captions_and_transitions()
    {
        if (!(ToolOnPath("ffmpeg") && ToolOnPath("ffprobe")))
        {
            Assert.False(Environment.GetEnvironmentVariable("REQUIRE_FFMPEG") == "1",
                "REQUIRE_FFMPEG=1 but ffmpeg/ffprobe were not detected on PATH");
            return;
        }

        var work = Directory.CreateTempSubdirectory("e2e-full-");
        try
        {
            // scene visuals: 2 "AI video" clips (moving test pattern) + 3 stills.
            var clip1 = await MakeClip(work, "clip1.mp4", 10, "testsrc2=s=1080x1920:r=30");
            var clip2 = await MakeClip(work, "clip2.mp4", 10, "smptehdbars=s=1080x1920:r=30");
            var img1 = await MakeStill(work, "img1.png", "red");
            var img2 = await MakeStill(work, "img2.png", "green");
            var img3 = await MakeStill(work, "img3.png", "navy");

            // narration: two segments male-pitched (180 Hz), then female-pitched (330 Hz).
            var vMale1 = await MakeVoice(work, "v1.wav", 10, 180);
            var vMale2 = await MakeVoice(work, "v2.wav", 14, 180);
            var vFemale1 = await MakeVoice(work, "v3.wav", 14, 330);
            var vFemale2 = await MakeVoice(work, "v4.wav", 10, 330);
            var vFemale3 = await MakeVoice(work, "v5.wav", 14, 330);

            var music = await MakeTone(work, "music.wav", 70, 220);
            var sfx = await MakeTone(work, "sfx.wav", 1, 900);
            var output = Path.Combine(work.FullName, "final.mp4");

            var audioValidator = new AudioValidator();
            var timing = new AudioTimingService();
            var captionSeg = new CaptionSegmentationService();
            var mixing = new AudioMixingService(Options.Create(new AudioMixOptions()));
            var renderer = new FfmpegVideoRenderer(mixing, NullLogger<FfmpegVideoRenderer>.Instance);
            var probe = new FfprobeMediaProbe(NullLogger<FfprobeMediaProbe>.Instance);
            var validator = new VideoQualityValidator(probe);
            var timeline = new TimelineService(
                captionSeg, mixing, Options.Create(new TimelineOptions()), Options.Create(new CaptionSegmentationOptions()));
            var composition = new VideoCompositionService(renderer, validator, NullLogger<VideoCompositionService>.Instance);

            var scenes = new (string Visual, bool Still, string Voice, string Narration)[]
            {
                (clip1, false, vMale1, "A stray tabby slipped through the office door on a rainy monday morning."),
                (img1, true, vMale2, "By wednesday it had its own chair and a spot in the standup rotation somehow."),
                (clip2, false, vFemale1, "Then the investors arrived, and the cat walked across the pitch deck at the exact right moment."),
                (img2, true, vFemale2, "The round closed that afternoon before anyone finished their coffee."),
                (img3, true, vFemale3, "The cat got a title, a dental plan, and a corner of the office. Follow for more."),
            };

            var inputs = new List<TimelineSceneInput>();
            var totalNarration = 0.0;
            for (var i = 0; i < scenes.Length; i++)
            {
                var (visual, still, voicePath, narration) = scenes[i];
                var v = audioValidator.Validate(await File.ReadAllBytesAsync(voicePath), "audio/wav");
                Assert.True(v.IsValid, v.Error);
                Assert.False(v.IsSilent);
                totalNarration += v.DurationSeconds;
                inputs.Add(new TimelineSceneInput(
                    SceneNumber: i + 1,
                    VisualAbsolutePath: visual,
                    IsStillImage: still,
                    VoiceAbsolutePath: voicePath,
                    NarrationTiming: timing.Compute(narration, v.DurationSeconds),
                    CaptionText: narration));
            }

            var built = timeline.Build(new TimelineBuildRequest(
                inputs,
                EnabledCaptions(),
                MusicPath: music,
                Sfx: new[] { new SfxCue(sfx, 3.0, 0.5) }));

            Assert.InRange(built.TotalSeconds, 55, 75);          // ~60s target window
            Assert.True(built.WithinTargetRange);
            Assert.NotEmpty(built.AllCues);
            Assert.Contains(built.Scenes, s => s.IsStillImage && s.Motion != SceneMotion.None); // image motion
            Assert.Contains(built.Scenes, s => !s.IsStillImage);                                 // AI video present
            Assert.Contains(built.Scenes, s => s.TransitionIn == TransitionKind.Fade);           // transitions

            // Render directly first so an ffmpeg failure surfaces its stderr
            // (VideoCompositionService deliberately swallows it into a report).
            var renderScenes = built.Scenes
                .OrderBy(s => s.StartSeconds)
                .Select(s => new RenderScene(s.VisualAbsolutePath, s.VoiceAbsolutePath, s.DurationSeconds,
                    Narration: "", s.IsStillImage, s.Motion, s.TransitionIn))
                .ToList();
            var render = await renderer.RenderAsync(new RenderRequest(
                renderScenes, music, output, EnabledCaptions(), built.AllCues,
                Width: 1080, Height: 1920, Fps: 30, AudioMix: built.Audio));

            var result = await composition.ComposeAsync(new CompositionRequest(
                Guid.NewGuid(), built, EnabledCaptions(), output,
                BackgroundMusicAbsolutePath: music,
                StoryboardSceneCount: scenes.Length,
                ScenesWithVisual: scenes.Length));

            Assert.True(result.Validation.IsValid, result.Validation.Summary);
            Assert.True(render.DurationSeconds > 0);

            var media = await probe.ProbeAsync(output);
            Assert.True(media.Ok, media.Error);
            Assert.True(media.HasVideo);
            Assert.True(media.HasAudio);                          // narration + music + sfx survived
            Assert.Equal(1080, media.Width);
            Assert.Equal(1920, media.Height);                     // 9:16
            Assert.InRange(media.DurationSeconds, 55, 75);        // ~55-75s
            Assert.True(Math.Abs(media.DurationSeconds - media.AudioDurationSeconds) < 1.5); // synchronized
            Assert.True(totalNarration > 0);
        }
        finally
        {
            try { work.Delete(recursive: true); } catch { /* ignore */ }
        }
    }

    private static async Task<string> MakeClip(DirectoryInfo dir, string name, int seconds, string source)
    {
        var path = Path.Combine(dir.FullName, name);
        await Run("ffmpeg", $"-y -loglevel error -f lavfi -i {source} -t {seconds} -pix_fmt yuv420p \"{path}\"");
        return path;
    }

    private static async Task<string> MakeStill(DirectoryInfo dir, string name, string colour)
    {
        var path = Path.Combine(dir.FullName, name);
        await Run("ffmpeg", $"-y -loglevel error -f lavfi -i color=c={colour}:s=1080x1920 -frames:v 1 \"{path}\"");
        return path;
    }

    private static async Task<string> MakeVoice(DirectoryInfo dir, string name, int seconds, int hz)
    {
        var path = Path.Combine(dir.FullName, name);
        await Run("ffmpeg", $"-y -loglevel error -f lavfi -i sine=frequency={hz}:duration={seconds} -ar 24000 -ac 1 \"{path}\"");
        return path;
    }

    private static async Task<string> MakeTone(DirectoryInfo dir, string name, int seconds, int hz)
    {
        var path = Path.Combine(dir.FullName, name);
        await Run("ffmpeg", $"-y -loglevel error -f lavfi -i sine=frequency={hz}:duration={seconds} -ar 44100 -ac 2 \"{path}\"");
        return path;
    }

    private static CaptionSettings EnabledCaptions() => CaptionSettings.Create(
        enabled: true, fontFamily: "DejaVu Sans", fontSizePt: 64,
        primaryColor: "#FFFFFF", highlightColor: "#FFD400", outlineColor: "#000000",
        outlineWidth: 3, shadowDepth: 0, bold: true, uppercase: false,
        position: CaptionPosition.Bottom, marginVerticalPx: 220,
        maxWordsPerCue: 5, animation: CaptionAnimation.PopIn, karaoke: true);

    private static CaptionSettings DisabledCaptions() => CaptionSettings.Create(
        enabled: false, fontFamily: "DejaVu Sans", fontSizePt: 60,
        primaryColor: "#FFFFFF", highlightColor: "#FFFFFF", outlineColor: "#000000",
        outlineWidth: 3, shadowDepth: 0, bold: true, uppercase: false,
        position: CaptionPosition.Bottom, marginVerticalPx: 220,
        maxWordsPerCue: 5, animation: CaptionAnimation.None, karaoke: false);

    private static bool ToolOnPath(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(tool, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p!.WaitForExit(5000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, $"{exe} {args}\n{stderr}");
    }
}
