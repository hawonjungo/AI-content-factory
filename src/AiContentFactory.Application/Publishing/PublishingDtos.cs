using AiContentFactory.Domain.Publishing;

namespace AiContentFactory.Application.Publishing;

public enum PublishMode { Now = 0, Schedule = 1 }

/// <param name="Platforms">Which platforms to publish to. Empty = nothing happens.</param>
/// <param name="ScheduledAtUtc">Required when <paramref name="Mode"/> is Schedule; must be in the future.</param>
/// <param name="PlatformPrivacy">
/// Per-platform privacy/visibility choice (e.g. TikTok "SELF_ONLY", YouTube
/// "unlisted"), keyed by platform. Each platform's own publisher interprets its
/// own value - there is no shared privacy enum, and picking a value for one
/// platform never affects another. Missing entries fall back to that
/// platform's own default.
/// </param>
public record PublishRequest(
    IReadOnlyList<PublishTarget> Platforms,
    string Title,
    string? Caption,
    string? Hashtags,
    PublishMode Mode,
    DateTimeOffset? ScheduledAtUtc,
    IReadOnlyDictionary<PublishTarget, string>? PlatformPrivacy = null);

/// <param name="Configured">The platform's client key/secret are present in config.</param>
/// <param name="AccountId">The connected target id (Facebook Page ID / Instagram user id / channel).</param>
/// <param name="Pages">When Status is PendingSelection, the targets the user can still choose from (Facebook).</param>
public record SocialConnectionDto(
    string Platform,
    string Status,
    string? AccountName,
    bool Configured,
    DateTimeOffset? ExpiresAtUtc,
    string? AccountId = null,
    IReadOnlyList<SocialPageDto>? Pages = null)
{
    public static SocialConnectionDto NotConnected(PublishTarget platform, bool configured) =>
        new(platform.ToString(), nameof(SocialConnectionStatus.Disconnected), null, configured, null);

    public static SocialConnectionDto FromDomain(SocialConnection c, bool configured, IReadOnlyList<SocialPageDto>? pages = null) =>
        new(c.Platform.ToString(), c.Status.ToString(), c.ExternalAccountName, configured, c.AccessTokenExpiresAt, c.ExternalAccountId, pages);
}

public record SocialPageDto(string Id, string Name);

/// <param name="SourceVideoUrl">
/// The same relative file URL the wizard's own "final video" points at
/// (/content-projects/{id}/assets/{videoAssetId}/file) - lets the UI tell
/// whether a Published job was for the video currently loaded, without
/// exposing the raw asset id anywhere. Two jobs sharing this value were
/// published from the exact same rendered file.
/// </param>
public record PublishJobDto(
    Guid Id,
    string Platform,
    string Status,
    string Title,
    DateTimeOffset? ScheduledAtUtc,
    DateTimeOffset? PublishedAtUtc,
    string? ExternalPostId,
    string? PublishedUrl,
    string? ErrorMessage,
    int AttemptCount,
    bool CanRetry,
    bool IsPermanentFailure,
    string? Privacy,
    string SourceVideoUrl)
{
    public static PublishJobDto FromDomain(PublishJob j) => new(
        j.Id,
        j.Platform.ToString(),
        j.Status.ToString(),
        j.Title,
        j.ScheduledAtUtc,
        j.PublishedAtUtc,
        j.ExternalPostId,
        j.PublishedUrl,
        j.ErrorMessage,
        j.AttemptCount,
        j.CanRetry,
        j.IsPermanentFailure,
        j.Privacy,
        $"/content-projects/{j.ContentProjectId}/assets/{j.VideoAssetId}/file");
}

/// <param name="Created">Jobs queued or scheduled by this request.</param>
/// <param name="Skipped">Platforms not acted on, with why (already active, not connected, not configured).</param>
public record PublishResponse(
    IReadOnlyList<PublishJobDto> Created,
    IReadOnlyList<PublishSkip> Skipped);

public record PublishSkip(string Platform, string Reason);

/// <summary>Raised when the request itself is invalid (no rendered video, bad schedule time, video fails a hard constraint). Carries per-item detail for the UI.</summary>
public sealed class PublishValidationException : Exception
{
    public PublishValidationException(IReadOnlyList<string> errors)
        : base(string.Join("; ", errors)) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}
