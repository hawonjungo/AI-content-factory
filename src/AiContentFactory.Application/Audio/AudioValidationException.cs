namespace AiContentFactory.Application.Audio;

/// <summary>
/// Thrown when generated narration audio is missing, unreadable, zero-length,
/// or silent. It is deliberately fatal: a scene with captions but no voice is
/// the exact failure this pipeline exists to prevent, so the generation fails
/// rather than quietly producing a silent clip.
/// </summary>
public class AudioValidationException : Exception
{
    public AudioValidationException(AudioValidationResult result)
        : base($"Narration audio failed validation: {result.Error ?? "unknown reason"}.")
    {
        Result = result;
    }

    public AudioValidationResult Result { get; }
}
