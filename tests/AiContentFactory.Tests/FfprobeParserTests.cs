using AiContentFactory.Infrastructure.Rendering;
using Xunit;

namespace AiContentFactory.Tests;

public class FfprobeParserTests
{
    private const string VideoAndAudio = """
    {
      "streams": [
        { "codec_type": "video", "width": 1080, "height": 1920, "avg_frame_rate": "30/1", "r_frame_rate": "30/1", "duration": "61.033000" },
        { "codec_type": "audio", "duration": "60.900000" }
      ],
      "format": { "duration": "61.050000" }
    }
    """;

    [Fact]
    public void Parses_a_vertical_video_with_audio()
    {
        var info = FfprobeParser.Parse(VideoAndAudio);

        Assert.True(info.Ok);
        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.Equal(1080, info.Width);
        Assert.Equal(1920, info.Height);
        Assert.Equal(30, info.FrameRate, 3);
        Assert.Equal(61.05, info.DurationSeconds, 2);
        Assert.Equal(60.9, info.AudioDurationSeconds, 2);
    }

    [Fact]
    public void Falls_back_to_r_frame_rate_when_avg_is_zero()
    {
        var json = """
        { "streams": [ { "codec_type": "video", "width": 1080, "height": 1920, "avg_frame_rate": "0/0", "r_frame_rate": "24/1" } ], "format": { "duration": "10.0" } }
        """;

        var info = FfprobeParser.Parse(json);

        Assert.Equal(24, info.FrameRate, 3);
    }

    [Fact]
    public void An_audioless_video_is_reported_without_audio()
    {
        var json = """
        { "streams": [ { "codec_type": "video", "width": 1080, "height": 1920, "avg_frame_rate": "30/1" } ], "format": { "duration": "12.5" } }
        """;

        var info = FfprobeParser.Parse(json);

        Assert.True(info.HasVideo);
        Assert.False(info.HasAudio);
        Assert.Equal(0, info.AudioDurationSeconds);
        Assert.Equal(12.5, info.DurationSeconds, 2);
    }

    [Fact]
    public void Audio_stream_without_its_own_duration_inherits_the_container_duration()
    {
        var json = """
        { "streams": [ { "codec_type": "video", "width": 1080, "height": 1920, "avg_frame_rate": "30/1" }, { "codec_type": "audio" } ], "format": { "duration": "45.0" } }
        """;

        var info = FfprobeParser.Parse(json);

        Assert.True(info.HasAudio);
        Assert.Equal(45.0, info.AudioDurationSeconds, 2);
    }
}
