namespace AiContentFactory.Domain.AssetReferences;

/// <summary>
/// The two kinds of consistency anchor a project can pin before generation.
/// Deliberately just two - a short-form video has one main character and one
/// main location; more than that stops being a "reference".
/// </summary>
public enum AssetReferenceType
{
    Character = 0,
    Environment = 1
}

public enum AssetReferenceStatus
{
    /// <summary>A generation was requested but hasn't produced an image yet.</summary>
    Pending = 0,

    /// <summary>An image exists (AI or upload) and is offered to the user as a variant to pick.</summary>
    Generated = 1,

    /// <summary>The user picked this image - it anchors every clip.</summary>
    Approved = 2,

    /// <summary>The user decided this type isn't needed (ImagePath stays null).</summary>
    Skipped = 3,

    /// <summary>A variant that wasn't chosen, or a previous approval replaced by a new one.</summary>
    Superseded = 4
}
