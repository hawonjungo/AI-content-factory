using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.Rendering;

/// <param name="Source">Which audio the scene segment carries.</param>
/// <param name="NeedsTts">A per-clip TTS voice track is required for this scene.</param>
/// <param name="UseClipDuration">
/// The scene should last as long as its own clip (Clip / Silent video scenes)
/// rather than being driven by narration timing.
/// </param>
public record SceneAudioDecision(SceneAudioSource Source, bool NeedsTts, bool UseClipDuration);

/// <summary>
/// Resolves, per clip, how the Step 6 Voice option applies. Pure and isolated so
/// the mixed-media matrix (Flow video with voice, image with narration, video
/// with no audio, silent image, mute) is unit-testable without ffmpeg.
/// </summary>
public static class SceneAudioResolver
{
    public static SceneAudioDecision Resolve(
        AudioMode mode,
        bool isStillImage,
        bool clipHasAudioStream,
        bool hasNarration)
        => mode switch
        {
            // Original pipeline: a voice-over bed for every narrated scene,
            // timing driven by the narration. Unchanged.
            AudioMode.Generated => new(SceneAudioSource.Voice, NeedsTts: hasNarration, UseClipDuration: false),

            // No audio at all; a video scene still lasts its own length.
            AudioMode.Muted => new(SceneAudioSource.Silent, NeedsTts: false, UseClipDuration: !isStillImage),

            // Smart / Auto and "Original Audio" - decided per clip. Same logic
            // today: keep a real audio stream, TTS the narrated gaps, else silent.
            _ => ResolveSmart(isStillImage, clipHasAudioStream, hasNarration),
        };

    private static SceneAudioDecision ResolveSmart(bool isStillImage, bool clipHasAudioStream, bool hasNarration)
    {
        // A still image never carries usable audio, so only a real video clip
        // with an actual audio stream keeps its original audio.
        if (!isStillImage && clipHasAudioStream)
        {
            return new(SceneAudioSource.Clip, NeedsTts: false, UseClipDuration: true);
        }

        // No original audio: fill the gap with TTS when there is something to say.
        if (hasNarration)
        {
            return new(SceneAudioSource.Voice, NeedsTts: true, UseClipDuration: false);
        }

        // Nothing to play - stay silent, but keep a video scene's own length.
        return new(SceneAudioSource.Silent, NeedsTts: false, UseClipDuration: !isStillImage);
    }
}
