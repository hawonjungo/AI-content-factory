using AiContentFactory.Domain.AssetReferences;

namespace AiContentFactory.Application.AssetReferences;

public record AssetReferenceVariantDto(Guid Id, string ImageUrl);

/// <param name="Status">"Pending" | "Generated" | "Approved" | "Skipped" - what the card shows.</param>
/// <param name="ImageUrl">API-relative path to the approved image, or null (Skipped / nothing yet).</param>
/// <param name="Variants">Generated images the user can still pick from.</param>
public record AssetReferenceSlotDto(
    string Type,
    string Status,
    string? ImageUrl,
    string? Prompt,
    IReadOnlyList<AssetReferenceVariantDto> Variants);

public record AssetReferenceSlotsDto(AssetReferenceSlotDto Character, AssetReferenceSlotDto Environment);

/// <summary>
/// One row of a project's FULL reference list, unlike <see cref="AssetReferenceSlotsDto"/>'s
/// fixed Character+Environment pair - includes every (Type, Label) row that
/// exists, including legacy rows with a null <see cref="Label"/>. Read-only;
/// used by the Story-linked multi-named-reference flow (see
/// <see cref="IAssetReferenceService.GetNamedReferencesAsync"/>).
/// </summary>
/// <param name="Label">
/// Which named character/location this row is for (e.g. "Milo", "Ha Long
/// Bay"), or null for the classic single-slot case.
/// </param>
/// <param name="Status">"Pending" | "Generated" | "Approved" | "Skipped".</param>
/// <param name="ImageUrl">API-relative path to the image, or null when there isn't one yet.</param>
public record NamedAssetReferenceDto(
    Guid Id,
    string Type,
    string? Label,
    string Status,
    string? ImageUrl,
    string? Prompt);

public record AssetReferenceResponse(Guid Id, string Type, string Status, string? ImageUrl, string? Prompt, DateTimeOffset CreatedAt)
{
    public static AssetReferenceResponse FromDomain(AssetReference reference, Func<Guid, string?> fileUrl) => new(
        reference.Id,
        reference.Type.ToString(),
        reference.Status.ToString(),
        reference.ImagePath is null ? null : fileUrl(reference.Id),
        reference.Prompt,
        reference.CreatedAt);
}

public record GenerateAssetReferenceRequest(string Type, int Count = 1, string? Prompt = null);

/// <param name="Prompt">The default prompt for this reference type, shown for review/edit before generation.</param>
/// <param name="ImageUsd">Configured per-image cost, so the UI can label the button without hardcoding.</param>
public record SuggestedReferencePromptResponse(string Prompt, string NegativePrompt, decimal ImageUsd);
