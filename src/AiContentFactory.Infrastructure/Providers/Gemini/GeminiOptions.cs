namespace AiContentFactory.Infrastructure.Providers.Gemini;

public class GeminiOptions
{
    public const string SectionName = "Llm:Gemini";

    /// <summary>
    /// Required. Set via Llm__Gemini__ApiKey (env var) or user-secrets locally -
    /// never commit a real key to appsettings.json.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Verify the current recommended model name against Google's docs before
    /// relying on this in production - model availability/naming changes.
    /// </summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>Nano Banana - Imagen was deprecated/shut down Aug 17 2026, this is the current recommended image model.</summary>
    public string ImageModel { get; set; } = "gemini-2.5-flash-image";

    public string TtsModel { get; set; } = "gemini-2.5-flash-preview-tts";

    /// <summary>One of Gemini's prebuilt voice names (e.g. Kore, Puck, Aoede) - see the TTS docs for the full gallery.</summary>
    public string TtsVoiceName { get; set; } = "Kore";

    /// <summary>Verify current model availability - Veo model names/tiers change independently of this codebase.</summary>
    public string VideoModel { get; set; } = "veo-3.1-fast-generate-preview";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Veo generation is a long-running operation (typically 1-3 minutes even for an 8s clip) - give it much more room than text/image calls.</summary>
    public int VideoTimeoutSeconds { get; set; } = 300;
}
