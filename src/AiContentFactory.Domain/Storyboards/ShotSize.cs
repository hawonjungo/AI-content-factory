namespace AiContentFactory.Domain.Storyboards;

/// <summary>
/// The framing (shot size) of a scene, chosen by the prompt agent from a fixed
/// set. Veo reads the shot size as the first "cinematography" ingredient of a
/// prompt, so a structured value keeps the wording deterministic.
/// <see cref="Unspecified"/> means no framing is stated (scenes prompted before
/// this existed keep their old prompt shape).
/// </summary>
public enum ShotSize
{
    Unspecified = 0,
    ExtremeWide = 1,
    Wide = 2,
    Medium = 3,
    MediumCloseUp = 4,
    CloseUp = 5,
    ExtremeCloseUp = 6
}
