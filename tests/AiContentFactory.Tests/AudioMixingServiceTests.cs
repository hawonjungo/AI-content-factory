using AiContentFactory.Application.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class AudioMixingServiceTests
{
    private readonly AudioMixingService _service = new(Options.Create(new AudioMixOptions()));

    [Fact]
    public void BuildSpec_carries_configured_defaults()
    {
        var spec = _service.BuildSpec(new AudioMixRequest("music.mp3", null, null, 60));

        Assert.Equal("music.mp3", spec.MusicPath);
        Assert.Equal(0.22, spec.MusicVolume, 3);
        Assert.Equal(0.4, spec.FadeInSeconds, 3);
        Assert.Equal(0.8, spec.FadeOutSeconds, 3);
        Assert.True(spec.HasBed);
    }

    [Fact]
    public void BuildSpec_with_nothing_extra_has_no_bed()
    {
        var spec = _service.BuildSpec(new AudioMixRequest(null, null, null, 60));

        Assert.False(spec.HasBed);
        Assert.Null(spec.MusicPath);
        Assert.Empty(spec.Sfx);
    }

    [Fact]
    public void Music_is_ducked_under_the_narration_via_sidechain_compression()
    {
        var spec = _service.BuildSpec(new AudioMixRequest("m.mp3", null, null, 62));
        var graph = _service.BuildFilterGraph(spec, new AudioInputLayout(0, 1, null, Array.Empty<int>()));

        Assert.Contains("sidechaincompress", graph.FilterComplex);
        // The narration bed is split so it can be both the mix and the sidechain key.
        Assert.Contains("asplit=2[narrmix][narrkey]", graph.FilterComplex);
        Assert.Contains("[music0][narrkey]sidechaincompress", graph.FilterComplex);
        Assert.Contains("amix=inputs=2", graph.FilterComplex);
        Assert.Equal("[aout]", graph.OutLabel);
    }

    [Fact]
    public void The_mix_fades_in_and_out_never_hard_cuts()
    {
        var spec = _service.BuildSpec(new AudioMixRequest("m.mp3", null, null, 62));
        var graph = _service.BuildFilterGraph(spec, new AudioInputLayout(0, 1, null, Array.Empty<int>()));

        Assert.Contains("afade=t=in", graph.FilterComplex);
        Assert.Contains("afade=t=out", graph.FilterComplex);
    }

    [Fact]
    public void With_no_bed_the_graph_is_just_the_narration_with_a_fade_out()
    {
        var spec = _service.BuildSpec(new AudioMixRequest(null, null, null, 40));
        var graph = _service.BuildFilterGraph(spec, new AudioInputLayout(0, null, null, Array.Empty<int>()));

        Assert.DoesNotContain("sidechaincompress", graph.FilterComplex);
        Assert.DoesNotContain("amix", graph.FilterComplex);
        Assert.Contains("afade=t=out", graph.FilterComplex);
        Assert.Contains("[aout]", graph.FilterComplex);
    }

    [Fact]
    public void Ambience_is_mixed_in_without_ducking()
    {
        var spec = _service.BuildSpec(new AudioMixRequest(null, null, "amb.wav", 55));
        var graph = _service.BuildFilterGraph(spec, new AudioInputLayout(0, null, 1, Array.Empty<int>()));

        Assert.Contains("amix=inputs=2", graph.FilterComplex);
        Assert.DoesNotContain("sidechaincompress", graph.FilterComplex);
    }

    [Fact]
    public void Sfx_are_delayed_to_their_cue_and_mixed()
    {
        var spec = _service.BuildSpec(new AudioMixRequest(
            null,
            new[] { new SfxCue("whoosh.wav", 3.5), new SfxCue("ding.wav", 12.0, 0.5) },
            null,
            60));
        var graph = _service.BuildFilterGraph(spec, new AudioInputLayout(0, null, null, new[] { 1, 2 }));

        Assert.Contains("adelay=3500|3500", graph.FilterComplex);
        Assert.Contains("adelay=12000|12000", graph.FilterComplex);
        Assert.Contains("amix=inputs=3", graph.FilterComplex);
    }

    [Fact]
    public void Full_bed_chains_music_then_ambience_then_sfx()
    {
        var spec = _service.BuildSpec(new AudioMixRequest(
            "m.mp3",
            new[] { new SfxCue("s.wav", 2.0) },
            "amb.wav",
            65));
        var graph = _service.BuildFilterGraph(spec, new AudioInputLayout(0, 1, 2, new[] { 3 }));

        Assert.Contains("sidechaincompress", graph.FilterComplex);
        Assert.Contains("adelay=2000|2000", graph.FilterComplex);
        Assert.Contains("[aout]", graph.FilterComplex);
    }
}
