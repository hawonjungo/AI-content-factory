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
/// lighting" string inside PromptAgent. <see cref="VisualStyleGuidance"/> /
/// <see cref="NegativePrompt"/> feed the per-clip Veo prompt and scene
/// keyframes, and are free to describe pose, expression, lighting, framing
/// and setting.
///
/// <see cref="ReferenceLookGuidance"/> / <see cref="ReferenceNegativePrompt"/>
/// are the LOOK-ONLY counterparts used exclusively when building a REFERENCE
/// image prompt (a character sheet on a plain backdrop, or an empty location
/// plate): art style, rendering medium, materials and color grade only - never
/// pose, expression, lighting setup, framing, camera, subject or setting - so a
/// style can never fight the neutral staging of a reference image. They are
/// deliberately required (no fallback to the full guidance) so a new preset
/// cannot silently leak scene language into reference prompts. Not exposed via
/// <see cref="StylePresetDto"/>.
/// </summary>
public record StylePreset(
    string Id,
    string Name,
    string Description,
    string VisualStyleGuidance,
    string? NegativePrompt,
    string ReferenceLookGuidance,
    string ReferenceNegativePrompt);

/// <summary>
/// Maps a human label onto one of Gemini's prebuilt TTS voices. GeminiVoiceName
/// must be a name the TTS API accepts - see the voice gallery in
/// https://ai.google.dev/gemini-api/docs/speech-generation. Speed, pitch and
/// language/accent are not baked in here - they are chosen per request via a
/// VoiceProfile so the same preset can be reused across languages.
/// </summary>
/// <param name="GeminiVoiceName">The provider voice id. A "kokoro:" prefix routes to the free local Kokoro server instead of Gemini.</param>
/// <param name="IsFree">A self-hosted voice with no per-call cost (Kokoro, English only).</param>
public record VoicePreset(
    string Id,
    string Name,
    string Description,
    string GeminiVoiceName,
    string StyleInstruction,
    VoiceGender Gender = VoiceGender.Unspecified,
    bool IsFree = false);

public record CaptionPreset(
    string Id,
    string Name,
    string Description,
    CaptionSettings Settings);
