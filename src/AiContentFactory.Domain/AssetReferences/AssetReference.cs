using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.AssetReferences;

/// <summary>
/// A character or environment reference image the user reviews and locks in
/// BEFORE any clip is generated, so the whole video stays visually consistent
/// (and so a bad reference is caught before it costs Veo credits).
///
/// One project has at most one Approved-or-Skipped row per Type (the
/// "resolved" state); several Generated rows can co-exist while the user is
/// choosing between AI variants. State changes go through
/// AssetReferenceRepository's atomic ExecuteUpdate helpers, not tracked
/// SaveChanges - see the repository for why.
/// </summary>
public class AssetReference : BaseEntity
{
    public Guid ContentProjectId { get; private set; }
    public AssetReferenceType Type { get; private set; }
    public AssetReferenceStatus Status { get; private set; } = AssetReferenceStatus.Pending;

    /// <summary>Application storage key (never an external URL). Null for a Skipped reference.</summary>
    public string? ImagePath { get; private set; }

    public string? Prompt { get; private set; }
    public string? Provider { get; private set; }

    private AssetReference()
    {
        // EF Core
    }

    public static AssetReference CreatePending(Guid contentProjectId, AssetReferenceType type, string? prompt, string? provider) => new()
    {
        ContentProjectId = contentProjectId,
        Type = type,
        Prompt = prompt,
        Provider = provider,
        Status = AssetReferenceStatus.Pending
    };

    public static AssetReference CreateGenerated(Guid contentProjectId, AssetReferenceType type, string imagePath, string? prompt, string? provider) => new()
    {
        ContentProjectId = contentProjectId,
        Type = type,
        ImagePath = imagePath,
        Prompt = prompt,
        Provider = provider,
        Status = AssetReferenceStatus.Generated
    };

    public static AssetReference CreateSkipped(Guid contentProjectId, AssetReferenceType type) => new()
    {
        ContentProjectId = contentProjectId,
        Type = type,
        Status = AssetReferenceStatus.Skipped
    };

    public void MarkGenerated(string imagePath)
    {
        ImagePath = imagePath;
        Status = AssetReferenceStatus.Generated;
        Touch();
    }

    public void Approve()
    {
        Status = AssetReferenceStatus.Approved;
        Touch();
    }

    public void Supersede()
    {
        Status = AssetReferenceStatus.Superseded;
        Touch();
    }
}
