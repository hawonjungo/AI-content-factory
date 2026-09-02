using System.Buffers.Binary;
using System.Text;

namespace AiContentFactory.Application.Audio;

/// <param name="IsValid">False whenever the pipeline should treat this as "no usable narration audio".</param>
/// <param name="IsSilent">True when the audio parsed but carries no audible signal (peak below the floor).</param>
public record AudioValidationResult(
    bool IsValid,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    double PeakAmplitude,
    bool IsSilent,
    string? Error)
{
    public static AudioValidationResult Invalid(string error) =>
        new(false, 0, 0, 0, 0, 0, false, error);
}

public interface IAudioValidator
{
    /// <summary>
    /// Parses a generated audio buffer (WAV/PCM) and decides whether it is
    /// usable narration: bytes present, header readable, a non-empty data
    /// chunk, duration &gt; 0, and - for 16-bit PCM - an audible peak above the
    /// silence floor.
    /// </summary>
    AudioValidationResult Validate(byte[]? audioBytes, string? mimeType = null);
}

/// <summary>
/// Pure WAV/PCM inspection - no external dependency, no decoder. Only the
/// common case (RIFF/WAVE, PCM or IEEE float, one or two channels) is
/// understood in full; anything it can't parse is reported as invalid rather
/// than assumed good, which is the safe direction for "fail if narration audio
/// is missing or invalid".
/// </summary>
public class AudioValidator : IAudioValidator
{
    /// <summary>16-bit sample magnitude below which a clip is treated as silent (~ -60 dBFS).</summary>
    public const double SilenceFloor16Bit = 32.0;

    private const int MinWavLength = 44; // canonical header size

    public AudioValidationResult Validate(byte[]? audioBytes, string? mimeType = null)
    {
        if (audioBytes is null || audioBytes.Length == 0)
        {
            return AudioValidationResult.Invalid("audio buffer is empty");
        }

        if (audioBytes.Length < MinWavLength)
        {
            return AudioValidationResult.Invalid($"audio buffer is only {audioBytes.Length} bytes - too short to be a WAV file");
        }

        var span = audioBytes.AsSpan();

        if (!Matches(span, 0, "RIFF") || !Matches(span, 8, "WAVE"))
        {
            return AudioValidationResult.Invalid("not a RIFF/WAVE container");
        }

        int audioFormat = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
        var haveFmt = false;
        var dataOffset = -1;
        var dataLength = 0;

        var pos = 12;
        while (pos + 8 <= audioBytes.Length)
        {
            var chunkId = Encoding.ASCII.GetString(audioBytes, pos, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos + 4, 4));
            var body = pos + 8;

            if (chunkSize < 0 || body + chunkSize > audioBytes.Length)
            {
                // Tolerate a truncated final data chunk: clamp to what's there.
                chunkSize = Math.Max(0, audioBytes.Length - body);
            }

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(body + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(body + 14, 2));
                haveFmt = true;
            }
            else if (chunkId == "data")
            {
                dataOffset = body;
                dataLength = chunkSize;
            }

            pos = body + chunkSize + (chunkSize % 2); // chunks are word-aligned
        }

        if (!haveFmt)
        {
            return AudioValidationResult.Invalid("WAV has no 'fmt ' chunk");
        }

        if (dataOffset < 0)
        {
            return AudioValidationResult.Invalid("WAV has no 'data' chunk");
        }

        if (dataLength <= 0)
        {
            return new AudioValidationResult(false, 0, sampleRate, channels, bitsPerSample, 0, true, "WAV 'data' chunk is empty");
        }

        if (channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
        {
            return AudioValidationResult.Invalid($"WAV format is unusable (channels={channels}, rate={sampleRate}, bits={bitsPerSample})");
        }

        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var duration = byteRate > 0 ? (double)dataLength / byteRate : 0;

        if (duration <= 0)
        {
            return new AudioValidationResult(false, 0, sampleRate, channels, bitsPerSample, 0, true, "computed audio duration is 0");
        }

        var (peak, isSilent) = MeasureLevel(span.Slice(dataOffset, dataLength), audioFormat, bitsPerSample);

        if (isSilent)
        {
            return new AudioValidationResult(false, duration, sampleRate, channels, bitsPerSample, peak, true,
                "audio parsed but is silent (no signal above the noise floor)");
        }

        return new AudioValidationResult(true, duration, sampleRate, channels, bitsPerSample, peak, false, null);
    }

    private static (double Peak, bool IsSilent) MeasureLevel(ReadOnlySpan<byte> data, int audioFormat, int bitsPerSample)
    {
        // 1 = PCM, 3 = IEEE float, 0xFFFE = extensible. Only 16-bit PCM is
        // scanned for silence; anything else is given the benefit of the doubt
        // (peak reported as -1, never flagged silent) so an exotic-but-real
        // format isn't rejected.
        if (audioFormat is not (1 or 0xFFFE) || bitsPerSample != 16)
        {
            return (-1, false);
        }

        double peak = 0;
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var sample = Math.Abs((int)BinaryPrimitives.ReadInt16LittleEndian(data.Slice(i, 2)));
            if (sample > peak)
            {
                peak = sample;
            }
        }

        return (peak, peak < SilenceFloor16Bit);
    }

    private static bool Matches(ReadOnlySpan<byte> span, int offset, string ascii)
    {
        if (offset + ascii.Length > span.Length)
        {
            return false;
        }

        for (var i = 0; i < ascii.Length; i++)
        {
            if (span[offset + i] != ascii[i])
            {
                return false;
            }
        }

        return true;
    }
}
