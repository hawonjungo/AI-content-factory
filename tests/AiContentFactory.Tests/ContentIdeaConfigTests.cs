using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Domain.ContentProjects;
using Xunit;

namespace AiContentFactory.Tests;

public class ContentIdeaConfigTests
{
    [Fact]
    public void Default_is_all_unset()
    {
        var config = ContentIdeaConfig.Default();

        Assert.Null(config.ContentPillar);
        Assert.Null(config.VoiceStyle);
        Assert.Null(config.SpeakingRate);
        Assert.Equal(VoiceGender.Unspecified, config.VoiceGender);
        Assert.Equal(CreditStrategy.Balanced, config.CreditStrategy);
    }

    [Fact]
    public void Create_trims_text_and_clamps_speaking_rate()
    {
        var config = ContentIdeaConfig.Create(
            "  founder lessons  ", "  first-time founders ", "case study", "bold claim", "tension",
            VoiceGender.Male, "  dramatic  ", speakingRate: 9.0, "  en-US ", CreditStrategy.MaxImpact);

        Assert.Equal("founder lessons", config.ContentPillar);
        Assert.Equal("first-time founders", config.TargetAudience);
        Assert.Equal("dramatic", config.VoiceStyle);
        Assert.Equal("en-US", config.NarrationLanguage);
        Assert.Equal(1.5, config.SpeakingRate); // clamped to the 0.5-1.5 useful range
        Assert.Equal(VoiceGender.Male, config.VoiceGender);
        Assert.Equal(CreditStrategy.MaxImpact, config.CreditStrategy);
    }

    [Fact]
    public void Blank_strings_become_null()
    {
        var config = ContentIdeaConfig.Create("   ", "", null, "  ", "",
            VoiceGender.Unspecified, "  ", null, "", CreditStrategy.Balanced);

        Assert.Null(config.ContentPillar);
        Assert.Null(config.HookStyle);
        Assert.Null(config.VoiceStyle);
    }

    [Fact]
    public void Dto_round_trips_through_the_domain()
    {
        var dto = new ContentIdeaConfigDto(
            "pillar", "audience", "myth-busting", "question", "awe",
            "Female", "warm", 1.1, "vi-VN", "Economy");

        var domain = dto.ToDomain();
        var back = ContentIdeaConfigDto.FromDomain(domain);

        Assert.Equal("pillar", back.ContentPillar);
        Assert.Equal("Female", back.VoiceGender);
        Assert.Equal("Economy", back.CreditStrategy);
        Assert.Equal(1.1, back.SpeakingRate!.Value, 3);
    }

    [Fact]
    public void Unknown_enum_strings_fall_back_to_the_safe_default()
    {
        var dto = new ContentIdeaConfigDto(null, null, null, null, null, "banana", null, null, null, "nonsense");

        var domain = dto.ToDomain();

        Assert.Equal(VoiceGender.Unspecified, domain.VoiceGender);
        Assert.Equal(CreditStrategy.Balanced, domain.CreditStrategy);
    }
}
