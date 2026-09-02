using AiContentFactory.Domain.Publishing;

namespace AiContentFactory.Application.Publishing;

/// <summary>
/// Deployment-level publishing config, bound from the "Publishing" section.
/// Provider client keys/secrets live in their own sections (Infrastructure) so
/// this stays free of secrets.
/// </summary>
public class PublishingOptions
{
    public const string SectionName = "Publishing";

    /// <summary>
    /// Public base URL of the API, e.g. "https://api.example.com". OAuth
    /// redirect URIs and the Instagram video pull-URL are built from this.
    /// Empty in local dev - OAuth then cannot complete and the UI says so.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Where the OAuth callback sends the browser back to (the wizard). Empty falls back to the first configured CORS origin.</summary>
    public string FrontendReturnUrl { get; set; } = string.Empty;

    /// <summary>
    /// Secret used to encrypt stored OAuth tokens (AES). Set a long random
    /// value in every non-local environment. Empty = tokens stored in plaintext
    /// with a startup warning (acceptable only for local dev).
    /// </summary>
    public string TokenEncryptionKey { get; set; } = string.Empty;

    public bool HasPublicBaseUrl => !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public string RedirectUriFor(PublishTarget platform) =>
        HasPublicBaseUrl
            ? $"{PublicBaseUrl.TrimEnd('/')}/social/connections/{PublishTargets.Slug(platform)}/callback"
            : string.Empty;
}

/// <summary>URL-friendly platform slugs, shared by the API routes and the redirect-URI builder.</summary>
public static class PublishTargets
{
    public static string Slug(PublishTarget platform) => platform switch
    {
        PublishTarget.TikTok => "tiktok",
        PublishTarget.YouTubeShorts => "youtube",
        PublishTarget.InstagramReels => "instagram",
        PublishTarget.FacebookPage => "facebook",
        _ => platform.ToString().ToLowerInvariant()
    };

    public static bool TryParse(string? value, out PublishTarget platform)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "tiktok":
                platform = PublishTarget.TikTok;
                return true;
            case "youtube":
            case "youtubeshorts":
            case "youtube-shorts":
                platform = PublishTarget.YouTubeShorts;
                return true;
            case "instagram":
            case "instagramreels":
            case "instagram-reels":
                platform = PublishTarget.InstagramReels;
                return true;
            case "facebook":
            case "facebookpage":
            case "facebook-page":
                platform = PublishTarget.FacebookPage;
                return true;
            default:
                return Enum.TryParse(value, ignoreCase: true, out platform);
        }
    }
}
