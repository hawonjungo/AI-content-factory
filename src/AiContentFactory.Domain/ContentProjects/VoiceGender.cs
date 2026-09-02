namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// Perceived narration voice gender, so the wizard can offer a plain
/// "male / female" choice on top of the named voice presets. Lives in the
/// domain because it is a persisted project-level narration choice
/// (<see cref="ContentIdeaConfig"/>), not just a preset attribute.
/// </summary>
public enum VoiceGender
{
    Unspecified = 0,
    Male = 1,
    Female = 2
}
