using AiContentFactory.Application.Providers;

namespace AiContentFactory.Tests.Fakes;

/// <summary>Captures the request and returns whatever <see cref="Responder"/> says.</summary>
public sealed class FakeTtsProvider : ITtsProvider
{
    public TtsRequest? LastRequest { get; private set; }
    public int Calls { get; private set; }

    public Func<TtsRequest, TtsResult> Responder { get; set; } =
        _ => new TtsResult(WavTestData.Pcm16(24000, 1.0, 8000), "audio/wav", 1.0, "fake-tts");

    public Task<TtsResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;
        return Task.FromResult(Responder(request));
    }
}

public sealed class FakeVideoGenerationProvider : IVideoGenerationProvider
{
    public VideoGenerationRequest? LastRequest { get; private set; }
    public int Calls { get; private set; }
    public Func<VideoGenerationRequest, VideoGenerationResult> Responder { get; set; } =
        r => new VideoGenerationResult(new byte[] { 1, 2, 3, 4 }, "video/mp4", r.Model ?? "fake-veo");
    public Exception? Throw { get; set; }

    public Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(Responder(request));
    }
}

public sealed class FakeImageGenerationProvider : IImageGenerationProvider
{
    public ImageGenerationRequest? LastRequest { get; private set; }
    public int Calls { get; private set; }
    public Func<ImageGenerationRequest, ImageGenerationResult> Responder { get; set; } =
        _ => new ImageGenerationResult(new byte[] { 9, 9, 9 }, "image/png", "fake-image");
    public Exception? Throw { get; set; }

    public Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(Responder(request));
    }
}
