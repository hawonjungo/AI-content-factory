using System.Buffers.Binary;
using System.Text;

namespace AiContentFactory.Tests.Fakes;

/// <summary>Builds tiny in-memory WAV buffers for audio-validation tests.</summary>
public static class WavTestData
{
    /// <summary>A valid 16-bit PCM WAV of the given length and (square-wave) amplitude. amplitude 0 = digital silence.</summary>
    public static byte[] Pcm16(int sampleRate = 24000, double seconds = 1.0, short amplitude = 8000, int channels = 1)
    {
        var frameCount = Math.Max(1, (int)Math.Round(sampleRate * seconds));
        var data = new byte[frameCount * channels * 2];

        for (var i = 0; i < frameCount * channels; i++)
        {
            var v = amplitude == 0 ? (short)0 : (i % 2 == 0 ? amplitude : (short)-amplitude);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2, 2), v);
        }

        return Wrap(data, sampleRate, channels, 16);
    }

    public static byte[] Silent(int sampleRate = 24000, double seconds = 1.0) => Pcm16(sampleRate, seconds, amplitude: 0);

    /// <summary>A structurally valid WAV whose data chunk is empty.</summary>
    public static byte[] EmptyData(int sampleRate = 24000) => Wrap(Array.Empty<byte>(), sampleRate, 1, 16);

    public static byte[] Empty() => Array.Empty<byte>();

    public static byte[] TooShort() => new byte[16];

    /// <summary>Right length, wrong container.</summary>
    public static byte[] NotRiff() => Enumerable.Repeat((byte)0x7A, 128).ToArray();

    private static byte[] Wrap(byte[] pcm, int sampleRate, int channels, int bitsPerSample)
    {
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write((short)bitsPerSample);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);

        return ms.ToArray();
    }
}
