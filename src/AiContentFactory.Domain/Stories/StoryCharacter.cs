using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Stories;

/// <summary>
/// A recurring character in a <see cref="Story"/>'s "bible", referenced by
/// future episodes for cast consistency.
/// </summary>
public class StoryCharacter : BaseEntity
{
    public Guid StoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Role/personality summary.</summary>
    public string? Description { get; private set; }

    /// <summary>Appearance description, for future reference-image consistency (not implemented yet).</summary>
    public string? VisualDescription { get; private set; }

    /// <summary>State of this character's reusable reference image, generated once and reused across every episode of the Story.</summary>
    public AssetReferenceStatus ReferenceImageStatus { get; private set; } = AssetReferenceStatus.Pending;

    /// <summary>Application storage key (never an external URL). Null until generated.</summary>
    public string? ReferenceImagePath { get; private set; }

    /// <summary>The prompt used to generate <see cref="ReferenceImagePath"/>.</summary>
    public string? ReferenceImagePrompt { get; private set; }

    public string? ReferenceImageProvider { get; private set; }

    /// <summary>Optional scoped anthropomorphic/species behavior profile applied when composing prompts for this character.</summary>
    public CharacterBehaviorProfile BehaviorProfile { get; private set; } = CharacterBehaviorProfile.None;

    /// <summary>Canonical classification (animal / anthropomorphic animal / human / other). Defaults to <see cref="CharacterKind.Unspecified"/>.</summary>
    public CharacterKind Kind { get; private set; } = CharacterKind.Unspecified;

    /// <summary>Canonical species/breed (e.g. "Scottish Fold cat"). <see cref="VisualDescription"/> stays the canonical appearance text.</summary>
    public string? Species { get; private set; }

    /// <summary>Canonical clothing and accessories that must stay consistent across episodes.</summary>
    public string? ClothingAndAccessories { get; private set; }

    /// <summary>Canonical distinctive features (scars, markings, ...) that must stay consistent across episodes.</summary>
    public string? DistinctiveFeatures { get; private set; }

    /// <summary>Where the current (<see cref="ReferenceImagePath"/>) reference image came from.</summary>
    public ReferenceImageSource ReferenceImageSource { get; private set; } = ReferenceImageSource.Generated;

    /// <summary>
    /// Candidate image awaiting the user's decision. Written instead of the main
    /// reference fields when a new image arrives while the current one is
    /// <see cref="AssetReferenceStatus.Approved"/>, so an approved image is never
    /// silently replaced. Application storage key, never an external URL.
    /// </summary>
    public string? PendingReferenceImagePath { get; private set; }

    public string? PendingReferenceImagePrompt { get; private set; }

    public string? PendingReferenceImageProvider { get; private set; }

    public ReferenceImageSource? PendingReferenceImageSource { get; private set; }

    /// <summary>True when a candidate image is waiting to be approved or discarded. Not persisted.</summary>
    public bool HasPendingReferenceImage => PendingReferenceImagePath is not null;

    public const int SpeciesMaxLength = 100;
    public const int ClothingAndAccessoriesMaxLength = 1000;
    public const int DistinctiveFeaturesMaxLength = 1000;

    private StoryCharacter()
    {
        // EF Core
    }

    public static StoryCharacter Create(
        Guid storyId,
        string name,
        string? description,
        string? visualDescription,
        CharacterBehaviorProfile behaviorProfile = CharacterBehaviorProfile.None,
        CharacterKind kind = CharacterKind.Unspecified,
        string? species = null,
        string? clothingAndAccessories = null,
        string? distinctiveFeatures = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        return new StoryCharacter
        {
            StoryId = storyId,
            Name = name.Trim(),
            Description = Normalize(description),
            VisualDescription = Normalize(visualDescription),
            BehaviorProfile = behaviorProfile,
            Kind = kind,
            Species = NormalizeWithLimit(species, SpeciesMaxLength, "Species"),
            ClothingAndAccessories = NormalizeWithLimit(clothingAndAccessories, ClothingAndAccessoriesMaxLength, "Clothing and accessories"),
            DistinctiveFeatures = NormalizeWithLimit(distinctiveFeatures, DistinctiveFeaturesMaxLength, "Distinctive features")
        };
    }

    /// <summary>
    /// <paramref name="behaviorProfile"/> defaults to <see cref="CharacterBehaviorProfile.None"/>
    /// like <see cref="Create"/> - callers editing name/description/appearance
    /// only MUST pass the character's current <see cref="BehaviorProfile"/>
    /// through explicitly, or this silently resets an existing opt-in back to
    /// <see cref="CharacterBehaviorProfile.None"/>.
    /// </summary>
    public void Update(
        string name,
        string? description,
        string? visualDescription,
        CharacterBehaviorProfile behaviorProfile = CharacterBehaviorProfile.None)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        Name = name.Trim();
        Description = Normalize(description);
        VisualDescription = Normalize(visualDescription);
        BehaviorProfile = behaviorProfile;
        Touch();
    }

    /// <summary>
    /// Updates the canonical identity fields. <c>null</c> leaves a value unchanged,
    /// a blank string clears a text field, a non-blank string is trimmed and set.
    /// Independent of <see cref="Update"/> so it cannot reset <see cref="BehaviorProfile"/>.
    /// </summary>
    public void UpdateCanonicalProfile(
        CharacterKind? kind,
        string? species,
        string? clothingAndAccessories,
        string? distinctiveFeatures)
    {
        // Validate everything first so a failure leaves the entity untouched.
        var newSpecies = species is null ? Species : NormalizeWithLimit(species, SpeciesMaxLength, "Species");
        var newClothing = clothingAndAccessories is null
            ? ClothingAndAccessories
            : NormalizeWithLimit(clothingAndAccessories, ClothingAndAccessoriesMaxLength, "Clothing and accessories");
        var newFeatures = distinctiveFeatures is null
            ? DistinctiveFeatures
            : NormalizeWithLimit(distinctiveFeatures, DistinctiveFeaturesMaxLength, "Distinctive features");

        if (kind is not null)
        {
            Kind = kind.Value;
        }

        Species = newSpecies;
        ClothingAndAccessories = newClothing;
        DistinctiveFeatures = newFeatures;
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

    /// <summary>
    /// Records a new image for this character. If the current image is not
    /// <see cref="AssetReferenceStatus.Approved"/> it behaves like the legacy
    /// overwrite (main fields, status = Generated, pending slot cleared). If the
    /// current image IS approved, only the pending candidate slot is written and
    /// the approved image, prompt and status stay untouched.
    /// </summary>
    public void SetReferenceCandidate(string imagePath, string? prompt, string? provider, ReferenceImageSource source)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            throw new DomainException("Image path is required.");
        }

        if (ReferenceImageStatus == AssetReferenceStatus.Approved)
        {
            PendingReferenceImagePath = imagePath;
            PendingReferenceImagePrompt = prompt;
            PendingReferenceImageProvider = provider;
            PendingReferenceImageSource = source;
        }
        else
        {
            ReferenceImagePath = imagePath;
            ReferenceImagePrompt = prompt;
            ReferenceImageProvider = provider;
            ReferenceImageSource = source;
            ReferenceImageStatus = AssetReferenceStatus.Generated;
            ClearPending();
        }

        Touch();
    }

    /// <summary>Makes the pending candidate the current, approved reference image and clears the pending slot.</summary>
    public void PromotePendingReferenceImage()
    {
        if (!HasPendingReferenceImage)
        {
            throw new DomainException("There is no pending reference image to approve.");
        }

        ReferenceImagePath = PendingReferenceImagePath;
        ReferenceImagePrompt = PendingReferenceImagePrompt;
        ReferenceImageProvider = PendingReferenceImageProvider;
        ReferenceImageSource = PendingReferenceImageSource ?? ReferenceImageSource.Generated;
        ReferenceImageStatus = AssetReferenceStatus.Approved;
        ClearPending();
        Touch();
    }

    /// <summary>Drops the pending candidate, keeping the current image. No-op when there is none.</summary>
    public void DiscardPendingReferenceImage()
    {
        if (!HasPendingReferenceImage)
        {
            return;
        }

        ClearPending();
        Touch();
    }

    private void ClearPending()
    {
        PendingReferenceImagePath = null;
        PendingReferenceImagePrompt = null;
        PendingReferenceImageProvider = null;
        PendingReferenceImageSource = null;
    }

    private static string? NormalizeWithLimit(string? value, int maxLength, string label)
    {
        var normalized = Normalize(value);
        if (normalized is not null && normalized.Length > maxLength)
        {
            throw new DomainException($"{label} must be at most {maxLength} characters.");
        }

        return normalized;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
