using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.Assets;

/// <summary>
/// A generated or imported media file. FilePath always points at
/// application-managed storage (see IFileStorage in Application) - provider
/// URLs are not assumed to be permanent and are copied in before this
/// entity is marked Ready.
/// </summary>
public class Asset : BaseEntity
{
    public Guid ContentProjectId { get; private set; }
    public Guid? SceneId { get; private set; }
    public AssetType Type { get; private set; }
    public string? FilePath { get; private set; }
    public string? Provider { get; private set; }
    public string? Prompt { get; private set; }
    public double? DurationSeconds { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public AssetStatus Status { get; private set; } = AssetStatus.Pending;

    private Asset()
    {
        // EF Core
    }

    public static Asset CreatePending(Guid contentProjectId, Guid? sceneId, AssetType type, string? provider, string? prompt) => new()
    {
        ContentProjectId = contentProjectId,
        SceneId = sceneId,
        Type = type,
        Provider = provider,
        Prompt = prompt
    };

    public void MarkGenerating()
    {
        Status = AssetStatus.Generating;
        Touch();
    }

    public void MarkReady(string filePath, double? durationSeconds, int? width, int? height)
    {
        FilePath = filePath;
        DurationSeconds = durationSeconds;
        Width = width;
        Height = height;
        Status = AssetStatus.Ready;
        Touch();
    }

    public void MarkFailed()
    {
        Status = AssetStatus.Failed;
        Touch();
    }
}
