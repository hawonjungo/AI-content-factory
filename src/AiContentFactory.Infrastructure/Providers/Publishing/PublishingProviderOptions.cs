namespace AiContentFactory.Infrastructure.Providers.Publishing;

/// <summary>Base for the three provider option classes - a client id/key + secret, plus a scope override.</summary>
public abstract class SocialProviderOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? Scope { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Bound from "Publishing:TikTok". <see cref="SocialProviderOptions.ClientId"/> is TikTok's <c>client_key</c>.</summary>
public class TikTokPublishOptions : SocialProviderOptions
{
    public const string SectionName = "Publishing:TikTok";
    public string ApiBaseUrl { get; set; } = "https://open.tiktokapis.com";
    public string AuthBaseUrl { get; set; } = "https://www.tiktok.com";

    /// <summary>
    /// True once TikTok has approved (audited) this app for the Content Posting
    /// API's public scopes. An unaudited app is restricted by TikTok to
    /// <c>privacy_level: "SELF_ONLY"</c> - posting anything else fails with
    /// <c>unaudited_client_can_only_post_to_private_accounts</c>. Defaults to
    /// false (safest) until the app passes TikTok's audit.
    /// </summary>
    public bool IsAudited { get; set; }

    /// <summary>
    /// Explicit <c>privacy_level</c> override ("PUBLIC_TO_EVERYONE",
    /// "MUTUAL_FOLLOW_FRIENDS", "FOLLOWER_OF_CREATOR", "SELF_ONLY"). Null derives
    /// it from <see cref="IsAudited"/> instead.
    /// </summary>
    public string? PrivacyLevel { get; set; }
}

/// <summary>Bound from "Publishing:YouTube". Uses a Google OAuth client (Data API v3, youtube.upload scope).</summary>
public class YouTubePublishOptions : SocialProviderOptions
{
    public const string SectionName = "Publishing:YouTube";
    public string UploadBaseUrl { get; set; } = "https://www.googleapis.com/upload/youtube/v3";
    public string OAuthTokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string OAuthAuthorizeUrl { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    /// <summary>YouTube category id - 22 = "People &amp; Blogs".</summary>
    public string CategoryId { get; set; } = "22";
    /// <summary>"public" | "unlisted" | "private". Scheduled uploads are forced to "private" until publishAt.</summary>
    public string PrivacyStatus { get; set; } = "public";
}

/// <summary>Bound from "Publishing:Instagram". Uses a Meta app (Graph API, instagram_content_publish).</summary>
public class InstagramPublishOptions : SocialProviderOptions
{
    public const string SectionName = "Publishing:Instagram";
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com/v21.0";
    public string OAuthAuthorizeUrl { get; set; } = "https://www.facebook.com/v21.0/dialog/oauth";
    /// <summary>Seconds to keep polling the media container's status before giving up.</summary>
    public int ContainerPollSeconds { get; set; } = 180;
}

/// <summary>
/// Bound from "Publishing:Facebook". Uses a Meta app (Graph API, Page video
/// publishing). Can be the SAME Meta app as Instagram - just different scopes.
/// </summary>
public class FacebookPublishOptions : SocialProviderOptions
{
    public const string SectionName = "Publishing:Facebook";
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com/v21.0";
    public string OAuthAuthorizeUrl { get; set; } = "https://www.facebook.com/v21.0/dialog/oauth";
    /// <summary>Seconds to poll the uploaded video's processing status before returning (the post still finishes server-side after a timeout).</summary>
    public int ProcessingPollSeconds { get; set; } = 120;
}
