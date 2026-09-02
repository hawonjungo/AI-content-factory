using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Publishing;

/// <summary>
/// One stored OAuth connection per platform (this app is single-tenant, so
/// there is at most one row per <see cref="PublishTarget"/>). Tokens are stored
/// already-encrypted by the application layer - the domain never sees plaintext
/// and never exposes tokens to the API surface.
/// </summary>
public class SocialConnection : BaseEntity
{
    public PublishTarget Platform { get; private set; }
    public SocialConnectionStatus Status { get; private set; } = SocialConnectionStatus.Disconnected;

    public string? ExternalAccountId { get; private set; }
    public string? ExternalAccountName { get; private set; }

    /// <summary>Ciphertext produced by ITokenProtector. Null when disconnected.</summary>
    public string? AccessTokenEncrypted { get; private set; }
    public string? RefreshTokenEncrypted { get; private set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; private set; }
    public string? Scope { get; private set; }

    /// <summary>
    /// Opaque, application-encrypted blob holding the candidate targets while the
    /// connection is <see cref="SocialConnectionStatus.PendingSelection"/> (e.g.
    /// the list of Facebook Pages + their Page tokens). Cleared once the user
    /// picks one via <see cref="SelectTarget"/>.
    /// </summary>
    public string? PendingSelectionData { get; private set; }

    private SocialConnection()
    {
        // EF Core
    }

    public static SocialConnection Create(PublishTarget platform) => new() { Platform = platform };

    public void Connect(
        string? externalAccountId,
        string? externalAccountName,
        string accessTokenEncrypted,
        string? refreshTokenEncrypted,
        DateTimeOffset? expiresAt,
        string? scope)
    {
        if (string.IsNullOrWhiteSpace(accessTokenEncrypted))
        {
            throw new DomainException("An access token is required to connect a social account.");
        }

        ExternalAccountId = externalAccountId;
        ExternalAccountName = externalAccountName;
        AccessTokenEncrypted = accessTokenEncrypted;
        RefreshTokenEncrypted = refreshTokenEncrypted;
        AccessTokenExpiresAt = expiresAt;
        Scope = scope;
        Status = SocialConnectionStatus.Connected;
        Touch();
    }

    /// <summary>
    /// OAuth succeeded but the account manages several publishable targets; park
    /// the encrypted candidate list until the user picks one.
    /// </summary>
    public void SetPendingSelection(string encryptedSelectionData)
    {
        if (string.IsNullOrWhiteSpace(encryptedSelectionData))
        {
            throw new DomainException("Pending selection data is required.");
        }

        ExternalAccountId = null;
        ExternalAccountName = null;
        AccessTokenEncrypted = null;
        RefreshTokenEncrypted = null;
        AccessTokenExpiresAt = null;
        PendingSelectionData = encryptedSelectionData;
        Status = SocialConnectionStatus.PendingSelection;
        Touch();
    }

    /// <summary>Finalises a <see cref="SocialConnectionStatus.PendingSelection"/> connection with the chosen target's id, name and (already-encrypted) token.</summary>
    public void SelectTarget(string targetId, string? targetName, string accessTokenEncrypted, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new DomainException("A target id is required.");
        }
        if (string.IsNullOrWhiteSpace(accessTokenEncrypted))
        {
            throw new DomainException("An access token is required.");
        }

        ExternalAccountId = targetId;
        ExternalAccountName = targetName;
        AccessTokenEncrypted = accessTokenEncrypted;
        RefreshTokenEncrypted = null;
        AccessTokenExpiresAt = expiresAt;
        PendingSelectionData = null;
        Status = SocialConnectionStatus.Connected;
        Touch();
    }

    /// <summary>Records a refreshed access token without disturbing the account identity / refresh token.</summary>
    public void UpdateAccessToken(string accessTokenEncrypted, string? refreshTokenEncrypted, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(accessTokenEncrypted))
        {
            throw new DomainException("An access token is required.");
        }

        AccessTokenEncrypted = accessTokenEncrypted;
        if (!string.IsNullOrWhiteSpace(refreshTokenEncrypted))
        {
            RefreshTokenEncrypted = refreshTokenEncrypted;
        }
        AccessTokenExpiresAt = expiresAt;
        Status = SocialConnectionStatus.Connected;
        Touch();
    }

    public void Disconnect()
    {
        AccessTokenEncrypted = null;
        RefreshTokenEncrypted = null;
        AccessTokenExpiresAt = null;
        Scope = null;
        PendingSelectionData = null;
        ExternalAccountId = null;
        ExternalAccountName = null;
        Status = SocialConnectionStatus.Disconnected;
        Touch();
    }

    public void MarkExpired()
    {
        Status = SocialConnectionStatus.Expired;
        Touch();
    }

    public bool IsUsable => Status == SocialConnectionStatus.Connected && !string.IsNullOrWhiteSpace(AccessTokenEncrypted);

    /// <summary>True when the access token is missing an expiry, or expires within the next 5 minutes.</summary>
    public bool NeedsRefresh(DateTimeOffset now) =>
        IsUsable && (AccessTokenExpiresAt is null || AccessTokenExpiresAt.Value <= now.AddMinutes(5));
}
