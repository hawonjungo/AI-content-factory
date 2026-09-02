namespace AiContentFactory.Application.Assets;

/// <summary>
/// Asset.Provider doubles as a discriminator for project-level assets that
/// share a Type: a caption-preview still and a generated thumbnail are both
/// project-level Images. These markers keep that convention in one place
/// instead of as repeated string literals. (Character/environment references
/// have moved to their own AssetReference table.)
/// </summary>
public static class AssetProviders
{
    /// <summary>The single still showing current caption styling; replaced on every preview request.</summary>
    public const string CaptionPreview = "ffmpeg:caption-preview";

    /// <summary>A user-uploaded file rather than a generated one.</summary>
    public const string Upload = "upload";
}
