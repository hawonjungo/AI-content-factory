using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.Presets;

// VoiceGender moved to AiContentFactory.Domain.ContentProjects (it is a
// persisted project-level choice now, see ContentIdeaConfig).

/// <summary>
/// A content format + niche the user picks in step 1 of the wizard. Carries
/// the guidance that gets folded into the script prompt, plus sensible
/// defaults for the other three preset slots so a user who picks a template
/// and nothing else still gets a coherent video.
/// </summary>
public record ContentTemplate(
    string Id,
    string Name,
    string Niche,
    string Description,
    int DefaultDurationSeconds,
    string DefaultAspectRatio,
    string ScriptGuidance,
    string DefaultStylePresetId,
    string DefaultVoicePresetId,
    string DefaultCaptionPresetId);

/// <summary>
/// Replaces what used to be a hardcoded "dark fantasy, cinematic, moody
/// lighting" string inside PromptAgent. Feeds both the per-clip Veo prompt
/// and the project's reference images, so the two can't drift apart.
/// </summary>
public record StylePreset(
    string Id,
    string Name,
    string Description,
    string VisualStyleGuidance,
    string? NegativePrompt);

/// <summary>
/// Maps a human label onto one of Gemini's prebuilt TTS voices. GeminiVoiceName
/// must be a name the TTS API accepts - see the voice gallery in
/// https://ai.google.dev/gemini-api/docs/speech-generation. Speed, pitch and
/// language/accent are not baked in here - they are chosen per request via a
/// VoiceProfile so the same preset can be reused across languages.
/// </summary>
public record VoicePreset(
    string Id,
    string Name,
    string Description,
    string GeminiVoiceName,
    string StyleInstruction,
    VoiceGender Gender = VoiceGender.Unspecified);

public record CaptionPreset(
    string Id,
    string Name,
    string Description,
    CaptionSettings Settings);
