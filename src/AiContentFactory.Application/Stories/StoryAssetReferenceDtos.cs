using System.Security.Cryptography;
using System.Text;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Stories;

/// <summary>
/// Short, stable identity of the pending candidate image (first 12 hex chars of the SHA-256 of its
/// storage key; null when there is none). Sent to the client so an approve can be tied to the
/// candidate the user actually looked at (see the expectedPendingVersion query of the approve endpoint).
/// </summary>
public static class ReferencePendingVersion
{
    public static string? Of(string? pendingImagePath)
    {
        if (string.IsNullOrWhiteSpace(pendingImagePath))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pendingImagePath)))[..12].ToLowerInvariant();
    }

    public static string? Of(StoryCharacter character) => Of(character.PendingReferenceImagePath);
}

/// <summary>
/// The reusable Story-level Character/Location reference image's current
/// state - the "GET .../file" endpoint serves <see cref="ImagePath"/>'s
/// bytes, this response is the metadata around it.
/// </summary>
/// <param name="HasPending">A candidate image is waiting to be approved or discarded (Character only; served by GET .../reference-image/pending/file).</param>
/// <param name="Source">"Generated" or "Uploaded" - where the current image came from (null for a Location, which has no upload path).</param>
/// <param name="PendingSource">Same, for the pending candidate (null when there is none).</param>
/// <param name="PendingVersion">Short stable identity of the pending candidate (null when there is none) - echo it as expectedPendingVersion when approving.</param>
public record StoryReferenceImageResponse(
    Guid Id,
    string Status,
    string? ImagePath,
    string? Prompt,
    string? Provider,
    bool HasPending = false,
    string? Source = null,
    string? PendingSource = null,
    string? PendingVersion = null)
{
    public static StoryReferenceImageResponse FromCharacter(StoryCharacter character) => new(
        character.Id,
        character.ReferenceImageStatus.ToString(),
        character.ReferenceImagePath,
        character.ReferenceImagePrompt,
        character.ReferenceImageProvider,
        character.HasPendingReferenceImage,
        character.ReferenceImageSource.ToString(),
        character.PendingReferenceImageSource?.ToString(),
        ReferencePendingVersion.Of(character));

    public static StoryReferenceImageResponse FromLocation(StoryLocation location) => new(
        location.Id,
        location.ReferenceImageStatus.ToString(),
        location.ReferenceImagePath,
        location.ReferenceImagePrompt,
        location.ReferenceImageProvider);
}

/// <summary>
/// GET .../characters/{characterId}/reference-prompt: the composed reference
/// prompt for one export target. No AI call is made and nothing is charged.
/// <see cref="NegativePrompt"/> is only set for target "inapp"; every other
/// target has the exclusions folded into <see cref="Prompt"/>.
/// </summary>
public record CharacterReferencePromptResponse(
    string Target,
    string Prompt,
    string? NegativePrompt,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> Notes);
