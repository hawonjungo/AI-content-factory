using AiContentFactory.Application.Presets;
using AiContentFactory.Domain.ContentProjects;
using Xunit;

namespace AiContentFactory.Tests;

public class VoiceCatalogTests
{
    [Fact]
    public void The_catalog_has_at_least_one_male_and_one_female_voice()
    {
        Assert.Contains(PresetCatalog.Voices, v => v.Gender == VoiceGender.Male);
        Assert.Contains(PresetCatalog.Voices, v => v.Gender == VoiceGender.Female);
    }

    [Fact]
    public void FindVoiceByGender_returns_a_voice_of_that_gender()
    {
        Assert.Equal(VoiceGender.Male, PresetCatalog.FindVoiceByGender(VoiceGender.Male)!.Gender);
        Assert.Equal(VoiceGender.Female, PresetCatalog.FindVoiceByGender(VoiceGender.Female)!.Gender);
        Assert.Null(PresetCatalog.FindVoiceByGender(VoiceGender.Unspecified));
    }

    [Fact]
    public void ResolveVoice_swaps_to_the_requested_gender_when_the_named_preset_does_not_match()
    {
        // "narrator-deep" is a female voice in the catalog.
        var resolved = PresetCatalog.ResolveVoice("narrator-deep", VoiceGender.Male);

        Assert.Equal(VoiceGender.Male, resolved.Gender);
    }

    [Fact]
    public void ResolveVoice_keeps_the_named_preset_when_the_gender_already_matches()
    {
        var resolved = PresetCatalog.ResolveVoice("energetic-host", VoiceGender.Male);

        Assert.Equal("energetic-host", resolved.Id);
    }

    [Fact]
    public void ResolveVoice_without_a_gender_preference_keeps_the_named_preset()
    {
        var resolved = PresetCatalog.ResolveVoice("warm-storyteller", VoiceGender.Unspecified);

        Assert.Equal("warm-storyteller", resolved.Id);
    }

    [Fact]
    public void ResolveVoice_falls_back_to_the_house_voice_for_an_unknown_id()
    {
        var resolved = PresetCatalog.ResolveVoice("no-such-voice", VoiceGender.Unspecified);

        Assert.Equal(PresetCatalog.FallbackVoiceId, resolved.Id);
    }
}
