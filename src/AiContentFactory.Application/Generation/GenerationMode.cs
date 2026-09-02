namespace AiContentFactory.Application.Generation;

/// <summary>
/// Which pipeline dresses the storyboard into clips.
/// </summary>
public enum GenerationMode
{
    /// <summary>
    /// Your clip plan, one Veo text-to-video call per clip. Any length, any
    /// clip count; costs the most and takes the longest.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// The "Google Flow" strategy: a fixed 3-scene, ~20-second hook script,
    /// a free Nano Banana image per scene, then Veo image-to-video (cheaper and
    /// faster per clip, but capped by a daily credit quota). Ignores the clip
    /// plan entirely - it writes its own.
    /// </summary>
    GoogleFlow = 1
}

public static class GenerationModeParser
{
    /// <summary>Lenient parse for the query-string value; unknown or empty falls back to Standard.</summary>
    public static GenerationMode Parse(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "googleflow" or "google-flow" or "flow" => GenerationMode.GoogleFlow,
        _ => GenerationMode.Standard
    };
}
