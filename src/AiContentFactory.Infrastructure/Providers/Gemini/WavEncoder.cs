namespace AiContentFactory.Infrastructure.Providers.Gemini;

/// <summary>
/// Gemini's TTS endpoint returns raw headerless PCM audio (typically 16-bit
/// mono), not a playable file - this wraps it in a minimal WAV container so
/// it can be written to disk and read by ffmpeg/browsers/media players.
/// </summary>
internal static class WavEncoder
{
    public static byte[] WrapPcmAsWav(byte[] pcmData, int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var dataSize = pcmData.Length;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16); // Subchunk1Size for PCM
        writer.Write((short)1); // AudioFormat = PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(pcmData);

        return stream.ToArray();
    }

    public static double CalculateDurationSeconds(byte[] pcmData, int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        var bytesPerSecond = sampleRate * channels * bitsPerSample / 8;
        return bytesPerSecond == 0 ? 0 : (double)pcmData.Length / bytesPerSecond;
    }
}
