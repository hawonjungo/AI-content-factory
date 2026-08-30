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
    public string Model { get; set; } = "gemini-3.6-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public int TimeoutSeconds { get; set; } = 60;
}
