using System.Text.Json;
using AiContentFactory.Domain.Publishing;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Publishing;

public interface ISocialConnectionService
{
    Task<IReadOnlyList<SocialConnectionDto>> GetStatusesAsync(CancellationToken cancellationToken = default);

    /// <summary>The provider's OAuth authorize URL to send the user's browser to. Throws if the platform is not configured or the app has no public base URL.</summary>
    Task<string> BuildAuthorizationUrlAsync(PublishTarget platform, string? redirectUriOverride, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges the OAuth code for tokens and stores the connection (tokens
    /// encrypted). If the account manages several targets (Facebook Pages) and
    /// exactly one is returned it is auto-selected; more than one leaves the
    /// connection in <see cref="SocialConnectionStatus.PendingSelection"/>.
    /// </summary>
    Task<SocialConnectionDto> CompleteAsync(PublishTarget platform, string code, string? redirectUriOverride, CancellationToken cancellationToken = default);

    /// <summary>Finalises a PendingSelection connection with the chosen target (Facebook Page) id.</summary>
    Task<SocialConnectionDto> SelectPageAsync(PublishTarget platform, string pageId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(PublishTarget platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a decrypted access token that is valid now (plus the connected
    /// account id, which some platforms need in the upload call), refreshing the
    /// token first if it is missing or about to expire. Throws when the platform
    /// is not connected or the refresh failed (the connection is marked Expired).
    /// </summary>
    Task<UsableAccessToken> GetUsableAccessTokenAsync(PublishTarget platform, CancellationToken cancellationToken = default);
}

public record UsableAccessToken(string AccessToken, string? AccountId);

public class SocialConnectionService : ISocialConnectionService
{
    private readonly ISocialConnectionRepository _connections;
    private readonly IReadOnlyDictionary<PublishTarget, ISocialPlatformPublisher> _publishers;
    private readonly ITokenProtector _tokenProtector;
    private readonly PublishingOptions _options;
    private readonly ILogger<SocialConnectionService> _logger;

    public SocialConnectionService(
        ISocialConnectionRepository connections,
        IEnumerable<ISocialPlatformPublisher> publishers,
        ITokenProtector tokenProtector,
        Microsoft.Extensions.Options.IOptions<PublishingOptions> options,
        ILogger<SocialConnectionService> logger)
    {
        _connections = connections;
        _publishers = publishers.ToDictionary(p => p.Platform);
        _tokenProtector = tokenProtector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SocialConnectionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var stored = (await _connections.GetAllAsync(cancellationToken)).ToDictionary(c => c.Platform);

        return Enum.GetValues<PublishTarget>()
            .Select(platform =>
            {
                var configured = _publishers.TryGetValue(platform, out var pub) && pub.IsConfigured;
                if (!stored.TryGetValue(platform, out var connection))
                {
                    return SocialConnectionDto.NotConnected(platform, configured);
                }

                IReadOnlyList<SocialPageDto>? pages = null;
                if (connection.Status == SocialConnectionStatus.PendingSelection && connection.PendingSelectionData is { } blob)
                {
                    pages = TryReadPending(blob)?.Pages.Select(p => new SocialPageDto(p.Id, p.Name)).ToList();
                }

                return SocialConnectionDto.FromDomain(connection, configured, pages);
            })
            .ToList();
    }

    public Task<string> BuildAuthorizationUrlAsync(PublishTarget platform, string? redirectUriOverride, CancellationToken cancellationToken = default)
    {
        var publisher = RequireConfigured(platform);
        var redirectUri = ResolveRedirectUri(platform, redirectUriOverride);
        // `state` guards against CSRF and carries the platform for the callback.
        var state = $"{PublishTargets.Slug(platform)}.{Guid.NewGuid():N}";
        return Task.FromResult(publisher.GetAuthorizationUrl(redirectUri, state));
    }

    public async Task<SocialConnectionDto> CompleteAsync(PublishTarget platform, string code, string? redirectUriOverride, CancellationToken cancellationToken = default)
    {
        var publisher = RequireConfigured(platform);
        var redirectUri = ResolveRedirectUri(platform, redirectUriOverride);

        var tokens = await publisher.ExchangeCodeAsync(code, redirectUri, cancellationToken);

        var connection = await _connections.GetAsync(platform, cancellationToken);
        if (connection is null)
        {
            connection = SocialConnection.Create(platform);
            await _connections.AddAsync(connection, cancellationToken);
        }

        var candidates = tokens.Accounts ?? Array.Empty<OAuthAccountOption>();
        if (candidates.Count == 0)
        {
            // Single-account platform (TikTok / YouTube / Instagram).
            connection.Connect(
                tokens.AccountId,
                tokens.AccountName,
                _tokenProtector.Protect(tokens.AccessToken),
                tokens.RefreshToken is null ? null : _tokenProtector.Protect(tokens.RefreshToken),
                tokens.ExpiresAt,
                tokens.Scope);
        }
        else if (candidates.Count == 1)
        {
            var only = candidates[0];
            connection.SelectTarget(
                only.Id, only.Name, _tokenProtector.Protect(only.AccessToken), only.ExpiresAt ?? tokens.ExpiresAt);
        }
        else
        {
            var pending = new PendingSelection(candidates
                .Select(a => new PendingSelection.Page(a.Id, a.Name, a.AccessToken, a.ExpiresAt ?? tokens.ExpiresAt))
                .ToList());
            connection.SetPendingSelection(_tokenProtector.Protect(JsonSerializer.Serialize(pending)));
        }

        await _connections.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "OAuth for {Platform} completed: status {Status} ({Account})",
            platform, connection.Status, connection.ExternalAccountName ?? $"{candidates.Count} target(s)");

        return await GetStatusForAsync(platform, cancellationToken);
    }

    public async Task<SocialConnectionDto> SelectPageAsync(PublishTarget platform, string pageId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetAsync(platform, cancellationToken)
            ?? throw new PublishException($"{platform} chưa được kết nối.", retryable: false);

        if (connection.Status != SocialConnectionStatus.PendingSelection || connection.PendingSelectionData is null)
        {
            throw new PublishException($"{platform} không ở trạng thái cần chọn Page.", retryable: false);
        }

        var pending = TryReadPending(connection.PendingSelectionData)
            ?? throw new PublishException("Dữ liệu chọn Page không hợp lệ - hãy kết nối lại.", retryable: false);

        var page = pending.Pages.FirstOrDefault(p => p.Id == pageId?.Trim())
            ?? throw new PublishException($"Không tìm thấy Page '{pageId}' trong tài khoản đã kết nối.", retryable: false);

        connection.SelectTarget(page.Id, page.Name, _tokenProtector.Protect(page.AccessToken), page.ExpiresAt);
        await _connections.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Selected {Platform} target {PageName} ({PageId})", platform, page.Name, page.Id);

        return await GetStatusForAsync(platform, cancellationToken);
    }

    private async Task<SocialConnectionDto> GetStatusForAsync(PublishTarget platform, CancellationToken cancellationToken) =>
        (await GetStatusesAsync(cancellationToken)).First(s => s.Platform == platform.ToString());

    private PendingSelection? TryReadPending(string encryptedBlob)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingSelection>(_tokenProtector.Unprotect(encryptedBlob));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read pending page-selection data");
            return null;
        }
    }

    private sealed record PendingSelection(IReadOnlyList<PendingSelection.Page> Pages)
    {
        public sealed record Page(string Id, string Name, string AccessToken, DateTimeOffset? ExpiresAt);
    }

    public async Task DisconnectAsync(PublishTarget platform, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetAsync(platform, cancellationToken);
        if (connection is null)
        {
            return;
        }

        connection.Disconnect();
        await _connections.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Disconnected {Platform} social account", platform);
    }

    public async Task<UsableAccessToken> GetUsableAccessTokenAsync(PublishTarget platform, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetAsync(platform, cancellationToken)
            ?? throw new PublishException($"{platform} chưa được kết nối. Hãy kết nối tài khoản ở Bước 7.", retryable: false);

        if (!connection.IsUsable)
        {
            throw new PublishException($"{platform} chưa được kết nối (hoặc token đã bị thu hồi).", retryable: false);
        }

        if (connection.NeedsRefresh(DateTimeOffset.UtcNow))
        {
            if (string.IsNullOrWhiteSpace(connection.RefreshTokenEncrypted))
            {
                connection.MarkExpired();
                await _connections.SaveChangesAsync(cancellationToken);
                throw new PublishException($"Phiên đăng nhập {platform} đã hết hạn, hãy kết nối lại.", retryable: false);
            }

            var publisher = RequireConfigured(platform);
            try
            {
                var refreshed = await publisher.RefreshAsync(
                    _tokenProtector.Unprotect(connection.RefreshTokenEncrypted!), cancellationToken);
                connection.UpdateAccessToken(
                    _tokenProtector.Protect(refreshed.AccessToken),
                    refreshed.RefreshToken is null ? null : _tokenProtector.Protect(refreshed.RefreshToken),
                    refreshed.ExpiresAt);
                await _connections.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                connection.MarkExpired();
                await _connections.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "Token refresh failed for {Platform}", platform);
                throw new PublishException($"Không làm mới được phiên {platform}, hãy kết nối lại.", retryable: false, ex);
            }
        }

        return new UsableAccessToken(_tokenProtector.Unprotect(connection.AccessTokenEncrypted!), connection.ExternalAccountId);
    }

    private ISocialPlatformPublisher RequireConfigured(PublishTarget platform)
    {
        if (!_publishers.TryGetValue(platform, out var publisher))
        {
            throw new InvalidOperationException($"No publisher registered for {platform}.");
        }

        if (!publisher.IsConfigured)
        {
            throw new PublishException(
                $"{platform} chưa được cấu hình (thiếu client key/secret). Xem README/.env.example.", retryable: false);
        }

        return publisher;
    }

    private string ResolveRedirectUri(PublishTarget platform, string? overrideUri)
    {
        if (!string.IsNullOrWhiteSpace(overrideUri))
        {
            return overrideUri.Trim();
        }

        var uri = _options.RedirectUriFor(platform);
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new PublishException(
                "Chưa cấu hình Publishing:PublicBaseUrl nên không tạo được redirect URI cho OAuth.", retryable: false);
        }

        return uri;
    }
}
