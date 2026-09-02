using System.Text.Json;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Publishing;

/// <summary>
/// Facebook Page video publishing via the Meta Graph API - a DIFFERENT flow from
/// Instagram Reels (different node, different token). Flow:
///   1. OAuth: facebook.com/dialog/oauth (scope pages_show_list, pages_manage_posts,
///      pages_read_engagement) -> code -> GET /oauth/access_token -> short-lived
///      USER token -> exchange (grant_type=fb_exchange_token) -> long-lived USER token.
///   2. GET /me/accounts?fields=id,name,access_token -> the Pages the user manages,
///      each with its own PAGE access token. If &gt;1, the user picks one (handled by
///      SocialConnectionService); the chosen Page id + Page token are stored.
///   3. Publish: POST /{page-id}/videos  file_url=&lt;public MP4 URL&gt;, title, description,
///      access_token=&lt;PAGE token&gt;  -> { id } (a video id).
///   4. Poll GET /{video-id}?fields=status until ready / error (best-effort).
///   5. GET /{video-id}?fields=permalink_url for the post URL.
/// The Graph API fetches the file from <c>file_url</c>, so the rendered MP4 MUST be
/// reachable over public HTTPS (Publishing:PublicBaseUrl). Docs:
/// https://developers.facebook.com/docs/video-api/guides/publishing
/// </summary>
public class FacebookPublisher : ISocialPlatformPublisher
{
    // Facebook Page video: up to ~10 GB and 4 hours - far larger than short-form needs.
    private const long MaxBytes = 10L * 1024 * 1024 * 1024;
    private const double MaxDurationSeconds = 4 * 60 * 60;

    private readonly HttpClient _http;
    private readonly FacebookPublishOptions _options;
    private readonly ILogger<FacebookPublisher> _logger;

    public FacebookPublisher(HttpClient http, IOptions<FacebookPublishOptions> options, ILogger<FacebookPublisher> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public PublishTarget Platform => PublishTarget.FacebookPage;
    public bool IsConfigured => _options.IsConfigured;

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var scope = string.IsNullOrWhiteSpace(_options.Scope)
            ? "pages_show_list,pages_manage_posts,pages_read_engagement"
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
        var shortLived = await GetJsonAsync(
            $"{_options.GraphBaseUrl}/oauth/access_token?client_id={Enc(_options.ClientId)}&client_secret={Enc(_options.ClientSecret)}&redirect_uri={Enc(redirectUri)}&code={Enc(code)}",
            cancellationToken, "OAuth code exchange");
        var shortToken = shortLived.RootElement.GetProperty("access_token").GetString()!;

        var longLived = await GetJsonAsync(
            $"{_options.GraphBaseUrl}/oauth/access_token?grant_type=fb_exchange_token&client_id={Enc(_options.ClientId)}&client_secret={Enc(_options.ClientSecret)}&fb_exchange_token={Enc(shortToken)}",
            cancellationToken, "long-lived token exchange");
        var userToken = longLived.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = longLived.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 60 * 24 * 3600;
        var userExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        var pagesDoc = await GetJsonAsync(
            $"{_options.GraphBaseUrl}/me/accounts?fields=id,name,access_token&limit=100&access_token={Enc(userToken)}",
            cancellationToken, "list Pages");

        var pages = new List<OAuthAccountOption>();
        if (pagesDoc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var p in data.EnumerateArray())
            {
                var id = p.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var name = p.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                var pageToken = p.TryGetProperty("access_token", out var tEl) ? tEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(pageToken))
                {
                    // Page tokens derived from a long-lived user token don't expire on
                    // their own; tie their lifetime to the user token for reconnect prompts.
                    pages.Add(new OAuthAccountOption(id!, name ?? id!, pageToken!, userExpiry));
                }
            }
        }

        if (pages.Count == 0)
        {
            throw new PublishException(
                "Tài khoản Facebook này không quản lý Page nào (hoặc thiếu quyền pages_show_list/pages_manage_posts).",
                retryable: false);
        }

        return new OAuthTokens(userToken, null, userExpiry, null, null, null, pages);
    }

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        throw new PublishException("Phiên Facebook đã hết hạn - hãy kết nối lại tài khoản và chọn Page.", retryable: false);

    public VideoConstraintResult ValidateVideo(MediaInfo probe, long sizeBytes)
    {
        var errors = new List<string>();
        if (sizeBytes > MaxBytes)
        {
            errors.Add("Facebook: tệp vượt quá 10 GB.");
        }
        if (probe.DurationSeconds > MaxDurationSeconds)
        {
            errors.Add($"Facebook: video dài {probe.DurationSeconds:0}s, tối đa {MaxDurationSeconds:0}s.");
        }
        return errors.Count == 0 ? VideoConstraintResult.Valid : new VideoConstraintResult(false, errors);
    }

    public async Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default)
    {
        var pageId = request.ExternalAccountId
            ?? throw new PublishException("Chưa chọn Facebook Page - hãy kết nối lại và chọn Page.", retryable: false);

        if (string.IsNullOrWhiteSpace(request.Video.PublicUrl))
        {
            throw new PublishException(
                "Facebook cần URL video công khai (đặt Publishing:PublicBaseUrl để Graph API tải video về).", retryable: false);
        }

        // 3. create the Page video from the public URL
        var form = new Dictionary<string, string>
        {
            ["file_url"] = request.Video.PublicUrl!,
            ["title"] = Trim(request.Metadata.Title, 255),
            ["description"] = TrimCaption(string.IsNullOrWhiteSpace(request.Metadata.Description)
                ? request.Metadata.Title
                : request.Metadata.Description),
            ["access_token"] = request.AccessToken
        };

        var created = await PostFormAsync($"{_options.GraphBaseUrl}/{pageId}/videos", form, cancellationToken, "tạo video trên Page");
        var videoId = created.RootElement.GetProperty("id").GetString()
            ?? throw new PublishException("Facebook không trả về video id.", retryable: true);

        // 4. best-effort poll for processing to finish (a timeout is fine - the post still publishes)
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(15, _options.ProcessingPollSeconds));
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            try
            {
                var st = await GetJsonAsync(
                    $"{_options.GraphBaseUrl}/{videoId}?fields=status&access_token={Enc(request.AccessToken)}",
                    cancellationToken, "kiểm tra trạng thái video");
                var phase = st.RootElement.TryGetProperty("status", out var status)
                    && status.TryGetProperty("video_status", out var vs) ? vs.GetString() : null;
                if (phase is "ready")
                {
                    break;
                }
                if (phase is "error")
                {
                    throw new PublishException("Facebook xử lý video thất bại (status error).", retryable: false);
                }
            }
            catch (PublishException pe) when (pe.Retryable)
            {
                // transient read error while polling - keep going until the deadline
            }
        }

        // 5. resolve the post URL
        var url = $"https://www.facebook.com/{pageId}/videos/{videoId}";
        try
        {
            var perma = await GetJsonAsync(
                $"{_options.GraphBaseUrl}/{videoId}?fields=permalink_url&access_token={Enc(request.AccessToken)}",
                cancellationToken, "lấy permalink");
            if (perma.RootElement.TryGetProperty("permalink_url", out var pu) && pu.GetString() is { } rel && rel.Length > 0)
            {
                url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : $"https://www.facebook.com{rel}";
            }
        }
        catch (PublishException)
        {
            // non-fatal - keep the constructed fallback URL
        }

        return new PublishResult(videoId, url);
    }

    // --- Graph HTTP/JSON helpers (Facebook Page publishing only) -------------

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
            throw new PublishException($"Facebook ({phase}) trả về phản hồi không phải JSON: {Trim(text, 300)}", retryable: true);
        }

        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
            var code = error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            // 1/2/4/17/32/613 = transient/rate-limit; 190 = token; 200/10/294 = permission -> permanent.
            var retryable = code is 1 or 2 or 4 or 17 or 32 or 613 || (int)resp.StatusCode >= 500;
            throw new PublishException($"Facebook ({phase}) lỗi {code}: {message}", retryable);
        }

        if (!resp.IsSuccessStatusCode)
        {
            throw new PublishException($"Facebook ({phase}) HTTP {(int)resp.StatusCode}", (int)resp.StatusCode >= 500);
        }

        return doc;
    }

    private static string Enc(string s) => Uri.EscapeDataString(s);
    private static string ToQuery(Dictionary<string, string?> parts) =>
        string.Join("&", parts.Where(p => p.Value is not null).Select(p => $"{Enc(p.Key)}={Enc(p.Value!)}"));
    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
    private static string TrimCaption(string s) => s.Length <= 5000 ? s : s[..5000];
}
