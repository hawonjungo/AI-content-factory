using System.Text;
using AiContentFactory.Application.Providers;

namespace AiContentFactory.IntegrationTests;

/// <summary>Deterministic script JSON so the pre-Flow steps run without a real LLM.</summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        Task.FromResult("""
        {"hook":"A stray cat walked into the office and nobody noticed.",
         "introduction":"This is the story of how a tabby became the most useful member of a startup.",
         "body":"It slipped in on a rainy monday. By wednesday it had its own chair. Engineers explained bugs to it and the bugs got fixed.",
         "escalation":"Then the investors visited and the cat walked across the pitch deck.",
         "payoff":"The round closed that afternoon. The cat got a title.",
         "callToAction":"Follow for more workplace disasters that somehow worked out."}
        """);
}

public sealed class FakeImageProvider : IImageGenerationProvider
{
    public Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ImageGenerationResult(OnePixelPng(), "image/png", "fake-image"));

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}

public sealed class FakeVideoProvider : IVideoGenerationProvider
{
    public Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Level 1 does not auto-generate video - clips come from Google Flow import.");
}

/// <summary>Returns a real 16-bit-mono sine WAV so AudioValidator accepts it.</summary>
public sealed class FakeTtsProvider : ITtsProvider
{
    public Task<TtsResult> GenerateAsync(TtsRequest request, CancellationToken cancellationToken = default)
    {
        var words = Math.Max(1, request.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var seconds = Math.Clamp(words / 2.5, 1.5, 20.0); // ~2.5 words/sec
        var wav = SineWav(seconds);
        return Task.FromResult(new TtsResult(wav, "audio/wav", seconds, "fake-tts"));
    }

    private static byte[] SineWav(double seconds, int sampleRate = 24000, int hz = 220)
    {
        var samples = (int)(sampleRate * seconds);
        var data = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var v = (short)(short.MaxValue * 0.4 * Math.Sin(2 * Math.PI * hz * i / sampleRate));
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + data.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(data.Length);
        w.Write(data);
        return ms.ToArray();
    }
}
