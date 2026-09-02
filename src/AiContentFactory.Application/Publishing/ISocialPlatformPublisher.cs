using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;

namespace AiContentFactory.Application.Publishing;

/// <param name="Title">Short title (YouTube uses it; TikTok/IG fold it into the caption).</param>
/// <param name="Description">Caption + hashtags, already combined.</param>
public record PublishMetadata(string Title, string Description);

/// <param name="OpenStream">Opens a fresh read stream over the rendered MP4. May be called more than once (resumable uploads).</param>
/// <param name="PublicUrl">
/// A publicly reachable HTTPS URL to the same file. Required by the Instagram
/// Graph API (it pulls the video from a URL); null when the deployment has no
/// public base URL configured.
/// </param>
public record PublishVideo(
    Func<CancellationToken, Task<Stream>> OpenStream,
    long SizeBytes,
    MediaInfo Probe,
    string? PublicUrl);

public record PublishUploadRequest(
    PublishMetadata Metadata,
    PublishVideo Video,
    string AccessToken,
    string? ExternalAccountId,
    /// <summary>When set, ask the platform to schedule the post for this UTC time instead of publishing immediately (YouTube supports this natively).</summary>
    DateTimeOffset? PublishAtUtc);

public record PublishResult(string ExternalId, string? Url);

/// <param name="ExpiresAt">Absolute expiry of <paramref name="AccessToken"/>, if the platform returns one.</param>
/// <param name="Accounts">
/// When the OAuth account manages several publishable targets (e.g. Facebook
/// Pages), the candidates with their per-target tokens. The connection service
/// auto-selects when there is exactly one, otherwise parks a "pick one" state.
/// Null/empty for single-account platforms.
/// </param>
public record OAuthTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? Scope,
    string? AccountId,
    string? AccountName,
    IReadOnlyList<OAuthAccountOption>? Accounts = null);

/// <param name="AccessToken">The per-target access token (e.g. a Facebook Page Access Token).</param>
public record OAuthAccountOption(string Id, string Name, string AccessToken, DateTimeOffset? ExpiresAt);

public record VideoConstraintResult(bool Ok, IReadOnlyList<string> Errors)
{
    public static VideoConstraintResult Valid { get; } = new(true, Array.Empty<string>());
    public static VideoConstraintResult Invalid(params string[] errors) => new(false, errors);
}

/// <summary>
/// Thrown by a publisher when an upload fails. <see cref="Retryable"/> tells the
/// worker whether a later retry could succeed (network blip, provider 5xx,
/// processing timeout) or never will (bad request, unsupported video, revoked
/// or missing scope) - a non-retryable failure is recorded and left alone.
/// </summary>
public sealed class PublishException : Exception
{
    public PublishException(string message, bool retryable, Exception? innerException = null)
        : base(message, innerException) => Retryable = retryable;

    public bool Retryable { get; }
}

/// <summary>
/// One social platform's real API integration, kept fully isolated: OAuth
/// (authorize URL, code exchange, refresh), platform-specific video constraints,
/// and the actual upload/publish call. Implementations live in Infrastructure.
/// </summary>
public interface ISocialPlatformPublisher
{
    PublishTarget Platform { get; }

    /// <summary>False when the platform's client key/secret are not configured - the UI shows "configure credentials" and no job is created.</summary>
    bool IsConfigured { get; }

    string GetAuthorizationUrl(string redirectUri, string state);

    Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Platform-specific checks (duration ceiling, size ceiling, container). The generic MP4/9:16/non-zero checks run before this.</summary>
    VideoConstraintResult ValidateVideo(MediaInfo probe, long sizeBytes);

    Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default);
}
