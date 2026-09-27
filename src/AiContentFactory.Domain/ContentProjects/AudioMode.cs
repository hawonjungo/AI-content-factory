namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// How the final video's audio is decided during composition (Step 6). Audio is
/// resolved <b>per clip</b>, not globally - see <c>SceneAudioResolver</c>.
///
/// Clips from Google Flow (or ones the user uploaded) usually carry their own
/// voice/audio, while AI image clips have none. The default keeps the original
/// audio where it exists and fills the gaps - and only the gaps - with TTS.
/// </summary>
public enum AudioMode
{
    /// <summary>
    /// Per clip: keep the clip's own audio when it has an audio stream; otherwise,
    /// if the clip has narration text, use TTS for that clip; otherwise stay
    /// silent. No TTS is generated for a clip that already has audio.
    /// </summary>
    Smart = 0,

    /// <summary>Generate an AI voice-over for every narrated clip and use it as the audio bed - the original pipeline behaviour.</summary>
    Generated = 1,

    /// <summary>Drop all audio (original and TTS). The final video has a silent audio track. Subtitles are unaffected.</summary>
    Muted = 2,

    /// <summary>
    /// Prefer the clip's own audio wherever a stream exists; fall back to the
    /// configured AI voice only for narrated clips that have no audio (e.g.
    /// still images). Behaves like <see cref="Smart"/> today; kept distinct so
    /// the intent ("keep original") is explicit and can diverge later.
    /// </summary>
    Original = 3
}
