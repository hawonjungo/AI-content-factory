namespace AiContentFactory.Domain.AssetReferences;

/// <summary>Where a reference image came from.</summary>
public enum ReferenceImageSource
{
    /// <summary>Produced by an image-generation provider.</summary>
    Generated = 0,

    /// <summary>Uploaded by the user.</summary>
    Uploaded = 1,
}
