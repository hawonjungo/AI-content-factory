using System.Text.Json;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Publishing;

/// <summary>
/// Instagram Reels via the Meta Graph API (Content Publishing). Flow:
///   1. OAuth: facebook.com/dialog/oauth (scope instagram_content_publish, instagram_basic,
///      pages_show_list, business_management) -> code -> GET /oauth/access_token
///      -> exchange for a long-lived token (grant_type=fb_exchange_token).
///      The connected IG business account id is resolved from /me/accounts.
///   2. POST /{ig-user-id}/media  media_type=REELS, video_url=&lt;public URL&gt;, caption
///      -> { id } (a media container).
///   3. Poll GET /{container-id}?fields=status_code until FINISHED / ERROR.
///   4. POST /{ig-user-id}/media_publish  creation_id=&lt;container-id&gt;  -> { id }.
///   5. GET /{id}?fields=permalink for the Reel URL.
/// Graph API pulls the video from <c>video_url</c>, so the rendered MP4 MUST be
/// reachable over public HTTPS (Publishing:PublicBaseUrl). Docs:
/// https://developers.facebook.com/docs/instagram-api/guides/content-publishing
/// </summary>
public class InstagramPublisher : ISocialPlatformPublisher
{
    // Graph API Reels: <= 15 minutes, <= 1 GB, 9:16.
    private const double MaxDurationSeconds = 900;
    private const long MaxBytes = 1024L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly InstagramPublishOptions _options;
    private readonly ILogger<InstagramPublisher> _logger;

    public InstagramPublisher(HttpClient http, IOptions<InstagramPublishOptions> options, ILogger<InstagramPublisher> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public PublishTarget Platform => PublishTarget.InstagramReels;
    public bool IsConfigured => _options.IsConfigured;

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var scope = string.IsNullOrWhiteSpace(_options.Scope)
            ? "instagram_basic,instagram_content_publish,pages_show_list,business_management"
            : _options.Scope;
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = state
        };
        return $"{_options.OAuthAuthorizeUrl}?{ToQuery(query)}";
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        // short-lived token
        var shortLived = await GetJsonAsync(
            $"{_options.GraphBaseUrl}/oauth/access_token?client_id={Enc(_options.ClientId)}&client_secret={Enc(_options.ClientSecret)}&redirect_uri={Enc(redirectUri)}&code={Enc(code)}",
            cancellationToken, "OAuth code exchange");
        var shortToken = shortLived.RootElement.GetProperty("access_token").GetString()!;

        // long-lived token (~60 days)
        var longLived = await GetJsonAsync(
            $"{_options.GraphBaseUrl}/oauth/access_token?grant_type=fb_exchange_token&client_id={Enc(_options.ClientId)}&client_secret={Enc(_options.ClientSecret)}&fb_exchange_token={Enc(shortToken)}",
            cancellationToken, "long-lived token exchange");
        var root = longLived.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 60 * 24 * 3600;

        var (igUserId, igName) = await ResolveInstagramAccountAsync(access, cancellationToken);

        return new OAuthTokens(access, null, DateTimeOffset.UtcNow.AddSeconds(expiresIn), null, igUserId, igName ?? "Instagram");
    }

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        // Meta long-lived tokens are refreshed by re-exchanging the current access token, not a refresh token.
        throw new PublishException("Phiên Instagram đã hết hạn - hãy kết nối lại tài khoản.", retryable: false);

    public VideoConstraintResult ValidateVideo(MediaInfo probe, long sizeBytes)
    {
        var errors = new List<string>();
        if (probe.DurationSeconds > MaxDurationSeconds)
        {
            errors.Add($"Instagram Reels: clip dài {probe.DurationSeconds:0}s, tối đa {MaxDurationSeconds:0}s.");
        }
        if (sizeBytes > MaxBytes)
        {
            errors.Add("Instagram Reels: tệp vượt quá 1 GB.");
        }
        return errors.Count == 0 ? VideoConstraintResult.Valid : new VideoConstraintResult(false, errors);
    }

    public async Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Video.PublicUrl))
        {
            throw new PublishException(
                "Instagram cần URL video công khai (đặt Publishing:PublicBaseUrl để API Meta tải video về).", retryable: false);
        }

        var igUserId = request.ExternalAccountId
            ?? throw new PublishException("Không xác định được Instagram Business Account - hãy kết nối lại.", retryable: false);

        // 2. create media container
        var createForm = new Dictionary<string, string>
        {
            ["media_type"] = "REELS",
            ["video_url"] = request.Video.PublicUrl!,
            ["caption"] = TrimCaption($"{request.Metadata.Title}\n{request.Metadata.Description}".Trim()),
            ["access_token"] = request.AccessToken
        };
        var create = await PostFormAsync($"{_options.GraphBaseUrl}/{igUserId}/media", createForm, cancellationToken, "tạo media container");
        var containerId = create.RootElement.GetProperty("id").GetString()
            ?? throw new PublishException("Instagram không trả về id container.", retryable: true);

        // 3. poll container status
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, _options.ContainerPollSeconds));
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var status = await GetJsonAsync(
                $"{_options.GraphBaseUrl}/{containerId}?fields=status_code&access_token={Enc(request.AccessToken)}",
                cancellationToken, "kiểm tra trạng thái container");
            var code = status.RootElement.TryGetProperty("status_code", out var sc) ? sc.GetString() : null;
            if (code is "FINISHED")
            {
                break;
            }
            if (code is "ERROR" or "EXPIRED")
            {
                throw new PublishException($"Instagram xử lý video thất bại (status {code}).", retryable: false);
            }
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new PublishException("Instagram xử lý video quá lâu (timeout container).", retryable: true);
            }
        }

        // 4. publish
        var publish = await PostFormAsync(
            $"{_options.GraphBaseUrl}/{igUserId}/media_publish",
            new Dictionary<string, string> { ["creation_id"] = containerId, ["access_token"] = request.AccessToken },
            cancellationToken, "media_publish");
        var mediaId = publish.RootElement.GetProperty("id").GetString()
            ?? throw new PublishException("Instagram media_publish không trả về id.", retryable: true);

        // 5. permalink (best-effort)
        string? permalink = null;
        try
        {
            var media = await GetJsonAsync(
                $"{_options.GraphBaseUrl}/{mediaId}?fields=permalink&access_token={Enc(request.AccessToken)}",
                cancellationToken, "lấy permalink");
            permalink = media.RootElement.TryGetProperty("permalink", out var p) ? p.GetString() : null;
        }
        catch (PublishException)
        {
            // non-fatal - the Reel is published, we just don't have its URL
        }

        return new PublishResult(mediaId, permalink);
    }

    private async Task<(string? IgUserId, string? Name)> ResolveInstagramAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var pages = await GetJsonAsync(
                $"{_options.GraphBaseUrl}/me/accounts?fields=name,instagram_business_account{{id,username}}&access_token={Enc(accessToken)}",
                cancellationToken, "resolve IG account");
            foreach (var page in pages.RootElement.GetProperty("data").EnumerateArray())
            {
                if (page.TryGetProperty("instagram_business_account", out var iga) && iga.TryGetProperty("id", out var id))
                {
                    var username = iga.TryGetProperty("username", out var u) ? u.GetString() : null;
                    return (id.GetString(), username ?? (page.TryGetProperty("name", out var n) ? n.GetString() : null));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve Instagram business account id during connect");
        }
        return (null, null);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken, string phase)
    {
        using var resp = await _http.GetAsync(url, cancellationToken);
        return await ReadGraphJsonAsync(resp, cancellationToken, phase);
    }

    private async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken, string phase)
    {
        using var resp = await _http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        return await ReadGraphJsonAsync(resp, cancellationToken, phase);
    }

    private static async Task<JsonDocument> ReadGraphJsonAsync(HttpResponseMessage resp, CancellationToken cancellationToken, string phase)
    {
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (JsonException)
        {
            throw new PublishException($"Instagram ({phase}) trả về phản hồi không phải JSON: {Trim(text)}", retryable: true);
        }

        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            var code = error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            // 4/17/32/613 = rate limiting/transient; 190 = token expired (permanent); 5xx = transient.
            var retryable = code is 4 or 17 or 32 or 613 or 2 || (int)resp.StatusCode >= 500;
            throw new PublishException($"Instagram ({phase}) lỗi {code}: {message}", retryable);
        }

        if (!resp.IsSuccessStatusCode)
        {
            throw new PublishException($"Instagram ({phase}) HTTP {(int)resp.StatusCode}", (int)resp.StatusCode >= 500);
        }

        return doc;
    }

    private static string Enc(string s) => Uri.EscapeDataString(s);
    private static string ToQuery(Dictionary<string, string?> parts) =>
        string.Join("&", parts.Where(p => p.Value is not null).Select(p => $"{Enc(p.Key)}={Enc(p.Value!)}"));
    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
    private static string TrimCaption(string s) => s.Length <= 2200 ? s : s[..2200];
}
