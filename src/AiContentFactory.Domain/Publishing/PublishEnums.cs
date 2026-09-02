namespace AiContentFactory.Domain.Publishing;

/// <summary>The short-form destinations Step 7 can publish to.</summary>
public enum PublishTarget
{
    TikTok = 0,
    YouTubeShorts = 1,
    InstagramReels = 2,
    /// <summary>A Facebook Page video post (separate from Instagram - own OAuth account, Page token and Graph node).</summary>
    FacebookPage = 3
}

public enum SocialConnectionStatus
{
    Disconnected = 0,
    Connected = 1,
    /// <summary>Had a token, but it expired and could not be refreshed - the user must reconnect.</summary>
    Expired = 2,
    /// <summary>OAuth succeeded but the account manages several targets (e.g. Facebook Pages) and the user still has to pick one.</summary>
    PendingSelection = 3
}

/// <summary>
/// Lifecycle of one video → one platform. Each (project, platform) pair is an
/// independent job so a TikTok failure never touches the YouTube result.
/// </summary>
public enum PublishJobStatus
{
    /// <summary>Publish-now job waiting for the worker to pick it up.</summary>
    Pending = 0,
    /// <summary>Scheduled job waiting for its scheduled time.</summary>
    Scheduled = 1,
    /// <summary>The worker is calling the platform API right now.</summary>
    Publishing = 2,
    /// <summary>Done - external id and (where available) URL recorded.</summary>
    Published = 3,
    /// <summary>Last attempt failed. Retryable unless <see cref="Publishing.PublishJob.IsPermanentFailure"/>.</summary>
    Failed = 4,
    Cancelled = 5
}
