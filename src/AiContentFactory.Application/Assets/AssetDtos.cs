using AiContentFactory.Domain.Assets;

namespace AiContentFactory.Application.Assets;

/// <summary>
/// Phase 2 only supports registering an asset that already exists somewhere
/// (e.g. you generated a clip manually in Flow and want to track it) -
/// FilePath is whatever you point it at for now. Real managed storage +
/// provider-driven generation land in Phase 4 (see IFileStorage).
/// </summary>
public record CreateAssetRequest(
    Guid? SceneId,
    AssetType Type,
    string? Provider,
    string? Prompt,
    string? FilePath,
    double? DurationSeconds,
    int? Width,
    int? Height);

public record AssetResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid? SceneId,
    string Type,
    string? FilePath,
    string? Provider,
    string? Prompt,
    double? DurationSeconds,
    int? Width,
    int? Height,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static AssetResponse FromDomain(Asset asset) => new(
        asset.Id,
        asset.ContentProjectId,
        asset.SceneId,
        asset.Type.ToString(),
        asset.FilePath,
        asset.Provider,
        asset.Prompt,
        asset.DurationSeconds,
        asset.Width,
        asset.Height,
        asset.Status.ToString(),
        asset.CreatedAt);
}
