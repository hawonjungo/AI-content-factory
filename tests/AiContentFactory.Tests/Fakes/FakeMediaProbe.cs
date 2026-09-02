using AiContentFactory.Application.Rendering;

namespace AiContentFactory.Tests.Fakes;

public sealed class FakeMediaProbe : IMediaProbe
{
    private readonly MediaInfo _info;

    public FakeMediaProbe(MediaInfo info) => _info = info;

    /// <summary>A well-formed vertical 1080x1920 clip with synchronized audio.</summary>
    public static MediaInfo GoodInfo(double duration = 61, int w = 1080, int h = 1920, double fps = 30, double? audio = null) =>
        new(true, null, duration, w, h, fps, HasVideo: true, HasAudio: true, AudioDurationSeconds: audio ?? duration);

    public Task<MediaInfo> ProbeAsync(string absolutePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(_info);
}
