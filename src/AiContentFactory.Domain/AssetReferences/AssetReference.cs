using AiContentFactory.Domain.Common;

namespace AiContentFactory.Domain.AssetReferences;

/// <summary>
/// A character or environment reference image the user reviews and locks in
/// BEFORE any clip is generated, so the whole video stays visually consistent
/// (and so a bad reference is caught before it costs Veo credits).
///
/// One project has at most one Approved-or-Skipped row per (Type, Label) -
/// one row per Type when <see cref="Label"/> is null (the classic single-slot
/// case, unchanged), or one row per Type per distinct Label when set (the
/// Story-linked, multi-named case, e.g. several Character rows for "Milo" and
/// "Mimi" on the same project). Several Generated rows can co-exist for the
/// same (Type, Label) while the user is choosing between AI variants. State
/// changes go through AssetReferenceRepository's atomic ExecuteUpdate
/// helpers, not tracked SaveChanges - see the repository for why.
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

    /// <summary>
    /// Name identifying WHICH named character/location (e.g. a
    /// <c>StoryCharacter</c>/<c>StoryLocation</c>'s Name) this reference is
    /// for, when a project holds more than one reference of the same
    /// <see cref="Type"/>. Null means "the classic single Character/Environment
    /// slot" - today's behavior, unchanged for every non-Story project.
    /// </summary>
    public string? Label { get; private set; }

    private AssetReference()
    {
        // EF Core
    }

    public static AssetReference CreatePending(Guid contentProjectId, AssetReferenceType type, string? prompt, string? provider, string? label = null) => new()
    {
        ContentProjectId = contentProjectId,
        Type = type,
        Prompt = prompt,
        Provider = provider,
        Status = AssetReferenceStatus.Pending,
        Label = NormalizeLabel(label)
    };

    public static AssetReference CreateGenerated(Guid contentProjectId, AssetReferenceType type, string imagePath, string? prompt, string? provider, string? label = null) => new()
    {
        ContentProjectId = contentProjectId,
        Type = type,
        ImagePath = imagePath,
        Prompt = prompt,
        Provider = provider,
        Status = AssetReferenceStatus.Generated,
        Label = NormalizeLabel(label)
    };

    public static AssetReference CreateSkipped(Guid contentProjectId, AssetReferenceType type, string? label = null) => new()
    {
        ContentProjectId = contentProjectId,
        Type = type,
        Status = AssetReferenceStatus.Skipped,
        Label = NormalizeLabel(label)
    };

    private static string? NormalizeLabel(string? label) => string.IsNullOrWhiteSpace(label) ? null : label.Trim();

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
