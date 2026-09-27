namespace AiContentFactory.Domain.Stories;

/// <summary>
/// Canonical "what is this character" classification, independent of the
/// free-text appearance description and of <see cref="CharacterBehaviorProfile"/>.
/// </summary>
public enum CharacterKind
{
    Unspecified = 0,
    Animal = 1,
    AnthropomorphicAnimal = 2,
    Human = 3,
    Other = 4,
}
