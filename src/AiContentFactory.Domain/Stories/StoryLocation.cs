using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Stories;

/// <summary>
/// A recurring location in a <see cref="Story"/>'s "bible", referenced by
/// future episodes for setting consistency.
/// </summary>
public class StoryLocation : BaseEntity
{
    public Guid StoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Appearance description, for future reference-image consistency (not implemented yet).</summary>
    public string? VisualDescription { get; private set; }

    /// <summary>State of this location's reusable reference image, generated once and reused across every episode of the Story.</summary>
    public AssetReferenceStatus ReferenceImageStatus { get; private set; } = AssetReferenceStatus.Pending;

    /// <summary>Application storage key (never an external URL). Null until generated.</summary>
    public string? ReferenceImagePath { get; private set; }

    /// <summary>The prompt used to generate <see cref="ReferenceImagePath"/>.</summary>
    public string? ReferenceImagePrompt { get; private set; }

    public string? ReferenceImageProvider { get; private set; }

    private StoryLocation()
    {
        // EF Core
    }

    public static StoryLocation Create(Guid storyId, string name, string? description, string? visualDescription)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        return new StoryLocation
        {
            StoryId = storyId,
            Name = name.Trim(),
            Description = Normalize(description),
            VisualDescription = Normalize(visualDescription)
        };
    }

    public void Update(string name, string? description, string? visualDescription)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        Name = name.Trim();
        Description = Normalize(description);
        VisualDescription = Normalize(visualDescription);
        Touch();
    }

    public void MarkReferenceImageGenerated(string imagePath, string? prompt, string? provider)
    {
        ReferenceImagePath = imagePath;
        ReferenceImagePrompt = prompt;
        ReferenceImageProvider = provider;
        ReferenceImageStatus = AssetReferenceStatus.Generated;
        Touch();
    }

    public void ApproveReferenceImage()
    {
        if (ReferenceImageStatus != AssetReferenceStatus.Generated)
        {
            throw new DomainException("Reference image must be generated before it can be approved.");
        }

        ReferenceImageStatus = AssetReferenceStatus.Approved;
        Touch();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
