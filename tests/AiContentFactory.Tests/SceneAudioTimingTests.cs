using AiContentFactory.Application.Audio;
using AiContentFactory.Domain.Storyboards;
using Xunit;

namespace AiContentFactory.Tests;

public class SceneAudioTimingTests
{
    private readonly AudioTimingService _service = new();

    [Fact]
    public void Timing_survives_a_serialize_deserialize_round_trip()
    {
        var timing = _service.Compute("The cat sat quietly on the warm windowsill for hours.", 4.6);

        var restored = _service.Deserialize(_service.Serialize(timing));

        Assert.True(restored.HasTiming);
        Assert.Equal(timing.TotalSeconds, restored.TotalSeconds, 3);
        Assert.Equal(timing.Words.Count, restored.Words.Count);
        Assert.Equal(timing.Sentences.Count, restored.Sentences.Count);
        Assert.Equal(timing.Words[0].Text, restored.Words[0].Text);
        Assert.Equal(timing.Words[^1].EndSeconds, restored.Words[^1].EndSeconds, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    public void Bad_or_missing_json_deserializes_to_empty_timing(string? json)
    {
        var restored = _service.Deserialize(json);

        Assert.False(restored.HasTiming);
        Assert.Equal(0, restored.TotalSeconds);
    }

    [Fact]
    public void Scene_persists_and_clears_its_audio_timing()
    {
        var storyboard = Storyboard.Create(Guid.NewGuid());
        var scene = storyboard.AddScene(8, "narration here", "shot", "push", SceneVisualType.AiVideo);
        var json = _service.Serialize(_service.Compute("narration here", 2.0));

        scene.SetAudioTiming(json);
        Assert.Equal(json, scene.AudioTimingJson);

        scene.SetAudioTiming("   ");
        Assert.Null(scene.AudioTimingJson);
    }
}
