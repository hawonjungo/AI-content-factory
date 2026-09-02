using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Agents;

/// <summary>
/// Generates viral short-form video scripts optimized for 20-second format with
/// fixed structure: 0-5s (Hook), 5-15s (Climax), 15-20s (CTA).
/// 
/// This agent uses Hook-Heavy structure to maximize engagement with minimal
/// credit consumption - each scene is designed for static image animation
/// via Veo 3.1 Image-to-Video with specific motion levels.
/// </summary>
public interface IHookScriptAgent
{
    /// <summary>
    /// Generates a 20-second hook-heavy script with exactly 3 scenes.
    /// </summary>
    Task<HookScriptResult> GenerateAsync(
        string topic,
        string audience,
        CancellationToken cancellationToken = default);
}

public class HookScriptAgent : IHookScriptAgent
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILogger<HookScriptAgent> _logger;

    public HookScriptAgent(ILlmProvider llmProvider, ILogger<HookScriptAgent> logger)
    {
        _llmProvider = llmProvider;
        _logger = logger;
    }

    public async Task<HookScriptResult> GenerateAsync(
        string topic,
        string audience,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating hook-heavy 20s script for topic: {Topic}", topic);

        var systemPrompt = """
You are a viral short-form video script writer specializing in TikTok, Reels, and YouTube Shorts.
Your task is to create scripts optimized for maximum engagement with a fixed 20-second structure.
Always respond with ONLY valid JSON (no markdown, no extra text).
""";

        var userPrompt = $$"""
Create a EXACTLY 20-second video script with this FIXED structure:

**CONSTRAINT: EXACTLY 3 SCENES, EXACTLY 20 SECONDS TOTAL**
- Scene 1 (0-5s / Hook): One shocking fact, psychological hook, or attention-grabbing question
- Scene 2 (5-15s / Climax): The payoff - peak of story, answer to question, or peak moment
- Scene 3 (15-20s / CTA): Call-to-action or loop that encourages engagement/sharing

**INPUT:**
Topic: {{topic}}
Target Audience: {{audience}}

**OUTPUT FORMAT (MUST BE VALID JSON - NO MARKDOWN):**
{
  "scenes": [
    {
      "sceneNumber": 1,
      "durationSeconds": 5,
      "sceneType": "Hook",
      "visualDescription": "Detailed photorealistic visual description for still image - 200+ words describing composition, colors, lighting, focal point, cinematic quality",
      "narration": "5-10 words punchy hook or shocking statement",
      "motionLevel": 1,
      "motionDirection": "Subtle zoom in, light camera pan, or ethereal smoke/particle effects"
    },
    {
      "sceneNumber": 2,
      "durationSeconds": 10,
      "sceneType": "Climax",
      "visualDescription": "Detailed visual description for image-to-video animation - 200+ words with clear action/movement potential, peak emotion, strong focal point",
      "narration": "15-25 words describing the climax/answer/payoff moment with emotional punch",
      "motionLevel": 3,
      "motionDirection": "Intense motion: character movement, action sequence, dramatic gesture, or visual transformation"
    },
    {
      "sceneNumber": 3,
      "durationSeconds": 5,
      "sceneType": "CTA",
      "visualDescription": "Detailed visual description - 200+ words showing urgency or curiosity, character reaction, product, or conclusive moment with emotional expression",
      "narration": "5-10 words direct call-to-action: Follow for more, Try this, Share this, or loop statement",
      "motionLevel": 1,
      "motionDirection": "Slow reveal or gentle emotional focus"
    }
  ],
  "totalDurationSeconds": 20,
  "targetAudience": "{{audience}}",
  "viralHook": "1-2 sentences explaining why this hook will stop scrollers",
  "callToActionType": "like|comment|share|follow|loop"
}
""";

        try
        {
            var result = await _llmProvider.GenerateAsync(systemPrompt, userPrompt, cancellationToken);
            var jsonContent = ExtractJson(result);

            // The JSON keys are camelCase but the record members are PascalCase -
            // without this the scene fields silently deserialize to null and the
            // validation below blames "missing narration".
            var hookScript = System.Text.Json.JsonSerializer.Deserialize<HookScriptResult>(
                jsonContent,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (hookScript == null)
            {
                throw new InvalidOperationException($"Hook script response did not parse as JSON. First 300 chars: {Truncate(jsonContent, 300)}");
            }

            // Validate structure
            if (hookScript.Scenes.Count != 3)
            {
                throw new InvalidOperationException($"Expected 3 scenes, got {hookScript.Scenes.Count}");
            }

            if (hookScript.TotalDurationSeconds != 20)
            {
                throw new InvalidOperationException($"Expected 20 seconds total, got {hookScript.TotalDurationSeconds}");
            }

            foreach (var scene in hookScript.Scenes)
            {
                if (string.IsNullOrWhiteSpace(scene.Narration))
                    throw new InvalidOperationException($"Scene {scene.SceneNumber} missing narration. Model returned: {Truncate(jsonContent, 400)}");
                if (string.IsNullOrWhiteSpace(scene.VisualDescription))
                    throw new InvalidOperationException($"Scene {scene.SceneNumber} missing visual description");
                if (string.IsNullOrWhiteSpace(scene.MotionDirection))
                    throw new InvalidOperationException($"Scene {scene.SceneNumber} missing motion direction");
            }

            _logger.LogInformation(
                "Hook script generated successfully: {Scenes} scenes, {Duration}s total",
                hookScript.Scenes.Count,
                hookScript.TotalDurationSeconds);

            return hookScript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate hook script");
            throw new AgentGenerationException("Hook script generation failed", ex);
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max] + "...";

    private static string ExtractJson(string response)
    {
        var startIdx = response.IndexOf('{');
        var endIdx = response.LastIndexOf('}');

        if (startIdx == -1 || endIdx == -1 || endIdx <= startIdx)
        {
            throw new InvalidOperationException("No valid JSON found in response");
        }

        return response[startIdx..(endIdx + 1)];
    }
}

public record HookScriptScene(
    int SceneNumber,
    int DurationSeconds,
    string SceneType,
    string VisualDescription,
    string Narration,
    int MotionLevel,
    string MotionDirection);

public record HookScriptResult(
    List<HookScriptScene> Scenes,
    int TotalDurationSeconds,
    string TargetAudience,
    string ViralHook,
    string CallToActionType)
{
    [System.Text.Json.Serialization.JsonPropertyName("scenes")]
    public List<HookScriptScene> Scenes { get; } = Scenes;

    [System.Text.Json.Serialization.JsonPropertyName("totalDurationSeconds")]
    public int TotalDurationSeconds { get; } = TotalDurationSeconds;

    [System.Text.Json.Serialization.JsonPropertyName("targetAudience")]
    public string TargetAudience { get; } = TargetAudience;

    [System.Text.Json.Serialization.JsonPropertyName("viralHook")]
    public string ViralHook { get; } = ViralHook;

    [System.Text.Json.Serialization.JsonPropertyName("callToActionType")]
    public string CallToActionType { get; } = CallToActionType;
}
