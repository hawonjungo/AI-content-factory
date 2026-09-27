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

    /// <summary>
    /// Caps generateContent output length - still a bounded ceiling (an
    /// unbounded max lets a misbehaving prompt or model run away with cost
    /// for no functional benefit), but 2048 proved too tight for
    /// ContinuityValidatorAgent's categorized multi-issue JSON output on a
    /// multi-episode Story: QA observed responses truncated mid-JSON
    /// (Gemini finishReason "MAX_TOKENS") once a validation call needed to
    /// enumerate several issues with detailed messages, causing
    /// AgentGenerationException on otherwise-correct calls. Raised to give
    /// that legitimate case headroom; short callers (ideas/hook/script) are
    /// bounded by their own prompts, not by this ceiling, so raising it
    /// doesn't change their behavior. Null keeps the model's own default (no
    /// cap sent).
    /// </summary>
    public int? MaxOutputTokens { get; set; } = 4096;

    /// <summary>Veo generation is a long-running operation (typically 1-3 minutes even for an 8s clip) - give it much more room than text/image calls.</summary>
    public int VideoTimeoutSeconds { get; set; } = 300;
}
