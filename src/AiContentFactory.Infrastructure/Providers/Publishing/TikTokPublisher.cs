using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Publishing;

/// <summary>
/// TikTok Content Posting API (direct post). Flow:
///   1. OAuth v2: authorize -> code -> POST /v2/oauth/token/ (grant_type=authorization_code|refresh_token).
///   2. POST /v2/post/publish/video/init/  with post_info + source_info(FILE_UPLOAD, single chunk)
///      -> { publish_id, upload_url }.
///   3. PUT the MP4 bytes to upload_url (Content-Range, video/mp4).
///   4. Poll POST /v2/post/publish/status/fetch/ with publish_id until PUBLISH_COMPLETE / FAILED.
/// Docs: https://developers.tiktok.com/doc/content-posting-api-get-started/
/// TikTok returns no canonical post URL from this API, so PublishResult.Url is null.
/// </summary>
public class TikTokPublisher : ISocialPlatformPublisher
{
    // Content Posting API: up to 4 GB, and (for most accounts) videos up to ~10 minutes.
    private const long MaxBytes = 4L * 1024 * 1024 * 1024;
    private const double MaxDurationSeconds = 600;

    private readonly HttpClient _http;
    private readonly TikTokPublishOptions _options;
    private readonly ILogger<TikTokPublisher> _logger;

    public TikTokPublisher(HttpClient http, IOptions<TikTokPublishOptions> options, ILogger<TikTokPublisher> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public PublishTarget Platform => PublishTarget.TikTok;
    public bool IsConfigured => _options.IsConfigured;

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var scope = string.IsNullOrWhiteSpace(_options.Scope) ? "video.publish,video.upload" : _options.Scope;
        var query = new Dictionary<string, string?>
        {
            ["client_key"] = _options.ClientId,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["redirect_uri"] = redirectUri,
            ["state"] = state
        };
        return $"{_options.AuthBaseUrl}/v2/auth/authorize/?{ToQuery(query)}";
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) =>
        await TokenRequestAsync(new Dictionary<string, string>
        {
            ["client_key"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        }, cancellationToken);

    public async Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        await TokenRequestAsync(new Dictionary<string, string>
        {
            ["client_key"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, cancellationToken);

    public VideoConstraintResult ValidateVideo(MediaInfo probe, long sizeBytes)
    {
        var errors = new List<string>();
        if (sizeBytes > MaxBytes)
        {
            errors.Add("TikTok: tệp vượt quá 4 GB.");
        }
        if (probe.DurationSeconds > MaxDurationSeconds)
        {
            errors.Add($"TikTok: clip dài {probe.DurationSeconds:0}s, tối đa {MaxDurationSeconds:0}s.");
        }
        return errors.Count == 0 ? VideoConstraintResult.Valid : new VideoConstraintResult(false, errors);
    }

    /// <summary>
    /// Queries TikTok for the connected creator's real, current publishing
    /// constraints (creator_info/query) and turns <c>privacy_level_options</c>
    /// into the picker the Step 7 UI shows - never a hard-coded guess. Called
    /// once when the UI loads the platform's settings, and again right before
    /// every upload (TikTok's own restrictions can change between the two).
    /// </summary>
    public async Task<PlatformPublishOptions> GetPublishOptionsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var info = await GetCreatorInfoAsync(accessToken, cancellationToken);
        return BuildPublishOptions(info);
    }

    private PlatformPublishOptions BuildPublishOptions(TikTokCreatorInfo info)
    {
        var options = info.PrivacyLevelOptions
            .Select(v => new PublishPrivacyOption(v, PrivacyLabel(v)))
            .ToList();

        var preferred = ResolvePrivacyLevel();
        var defaultPrivacy = options.Any(o => o.Value == preferred)
            ? preferred
            : options.FirstOrDefault(o => o.Value == "SELF_ONLY")?.Value ?? options.FirstOrDefault()?.Value;

        var canPostPublicly = options.Any(o => o.Value == "PUBLIC_TO_EVERYONE");
        var notice = !canPostPublicly
            ? "TikTok app này chưa được TikTok audit nên chỉ được đăng ở chế độ Chỉ mình tôi (SELF_ONLY). " +
              "Để đăng công khai, app phải hoàn tất quy trình audit của TikTok."
            : null;

        _logger.LogInformation(
            "TikTok creator_info for {Username}: privacy_level_options=[{Options}], resolved default={Default}",
            info.Username ?? "(unknown)", string.Join(",", info.PrivacyLevelOptions), defaultPrivacy ?? "(none)");

        return new PlatformPublishOptions(options, defaultPrivacy, notice, AccountLabel(info));
    }

    private static string? AccountLabel(TikTokCreatorInfo info) =>
        string.IsNullOrWhiteSpace(info.Username) ? null : $"@{info.Username}";

    private static string PrivacyLabel(string value) => value switch
    {
        "PUBLIC_TO_EVERYONE" => "Công khai",
        "MUTUAL_FOLLOW_FRIENDS" => "Bạn bè theo dõi nhau",
        "FOLLOWER_OF_CREATOR" => "Người theo dõi",
        "SELF_ONLY" => "Chỉ mình tôi",
        _ => value
    };

    private sealed record TikTokCreatorInfo(string? Username, IReadOnlyList<string> PrivacyLevelOptions);

    /// <summary>POST /v2/post/publish/creator_info/query/ - the source of truth for what privacy levels this creator/app combination may actually use right now.</summary>
    private async Task<TikTokCreatorInfo> GetCreatorInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/v2/post/publish/creator_info/query/")
        {
            Content = JsonContent.Create(new { })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, cancellationToken);
        var json = await ReadJsonAsync(resp, cancellationToken);
        ThrowOnTikTokError(resp, json, "creator_info");

        var data = json.RootElement.GetProperty("data");
        var username = data.TryGetProperty("creator_username", out var u) ? u.GetString() : null;
        var privacyOptions = data.TryGetProperty("privacy_level_options", out var po) && po.ValueKind == JsonValueKind.Array
            ? po.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).Select(s => s!).ToList()
            : new List<string>();

        return new TikTokCreatorInfo(username, privacyOptions);
    }

    public async Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default)
    {
        var size = request.Video.SizeBytes;

        // TikTok, not our own hard-coded default, is the source of truth for
        // which privacy_level values this creator/app combination may use
        // right now - an unaudited app is typically limited to SELF_ONLY, but
        // we ask instead of assuming.
        var creatorInfo = await GetCreatorInfoAsync(request.AccessToken, cancellationToken);
        var privacyLevel = ResolveRequestedPrivacy(request.Privacy, creatorInfo);
        _logger.LogInformation(
            "TikTok upload for {Username}: requested={Requested}, live privacy_level_options=[{Options}], sending privacy_level={Resolved}",
            creatorInfo.Username ?? "(unknown)", request.Privacy ?? "(default)", string.Join(",", creatorInfo.PrivacyLevelOptions), privacyLevel);

        // 1. init
        var initBody = new
        {
            post_info = new
            {
                title = Trim2200($"{request.Metadata.Title}\n{request.Metadata.Description}".Trim()),
                privacy_level = privacyLevel,
                disable_comment = false,
                disable_duet = false,
                disable_stitch = false
            },
            source_info = new
            {
                source = "FILE_UPLOAD",
                video_size = size,
                chunk_size = size,
                total_chunk_count = 1
            }
        };

        using var initReq = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/v2/post/publish/video/init/")
        {
            Content = JsonContent.Create(initBody)
        };
        initReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);

        using var initResp = await _http.SendAsync(initReq, cancellationToken);
        var initJson = await ReadJsonAsync(initResp, cancellationToken);
        ThrowOnTikTokError(initResp, initJson, "init", sentPrivacyLevel: privacyLevel);

        var data = initJson.RootElement.GetProperty("data");
        var publishId = data.GetProperty("publish_id").GetString()
            ?? throw new PublishException("TikTok init không trả về publish_id.", retryable: true);
        var uploadUrl = data.GetProperty("upload_url").GetString()
            ?? throw new PublishException("TikTok init không trả về upload_url.", retryable: true);

        // 2. PUT the bytes
        await using (var stream = await request.Video.OpenStream(cancellationToken))
        {
            using var putReq = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new StreamContent(stream) };
            putReq.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            putReq.Content.Headers.ContentLength = size;
            putReq.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, size - 1, size);

            using var putResp = await _http.SendAsync(putReq, cancellationToken);
            if (!putResp.IsSuccessStatusCode)
            {
                var body = await putResp.Content.ReadAsStringAsync(cancellationToken);
                throw new PublishException($"TikTok upload thất bại ({(int)putResp.StatusCode}): {Trim(body)}", retryable: true);
            }
        }

        // 3. poll status
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);

            using var statusReq = new HttpRequestMessage(HttpMethod.Post, $"{_options.ApiBaseUrl}/v2/post/publish/status/fetch/")
            {
                Content = JsonContent.Create(new { publish_id = publishId })
            };
            statusReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);

            using var statusResp = await _http.SendAsync(statusReq, cancellationToken);
            var statusJson = await ReadJsonAsync(statusResp, cancellationToken);
            ThrowOnTikTokError(statusResp, statusJson, "status");

            var status = statusJson.RootElement.GetProperty("data").GetProperty("status").GetString();
            if (status is "PUBLISH_COMPLETE")
            {
                return new PublishResult(publishId, null);
            }
            if (status is "FAILED")
            {
                var reason = statusJson.RootElement.GetProperty("data").TryGetProperty("fail_reason", out var fr) ? fr.GetString() : "unknown";
                throw new PublishException($"TikTok từ chối video: {reason}", retryable: false);
            }
        }

        throw new PublishException("TikTok xử lý video quá lâu (timeout chờ PUBLISH_COMPLETE).", retryable: true);
    }

    /// <summary>
    /// This app's own preference (config override, or its audit status) - used
    /// only as a tie-breaker when picking a default among TikTok's actual
    /// <c>privacy_level_options</c> for this creator. Never sent as-is without
    /// checking it against that list first: unaudited apps are typically
    /// restricted to "SELF_ONLY" - anything else fails with
    /// unaudited_client_can_only_post_to_private_accounts.
    /// </summary>
    private string ResolvePrivacyLevel() =>
        !string.IsNullOrWhiteSpace(_options.PrivacyLevel)
            ? _options.PrivacyLevel!
            : _options.IsAudited ? "PUBLIC_TO_EVERYONE" : "SELF_ONLY";

    /// <summary>
    /// Turns the user's chosen privacy (if any) plus TikTok's live
    /// <c>privacy_level_options</c> into the value actually sent to
    /// <c>video/init</c>. Never falls back to a value TikTok didn't offer.
    /// </summary>
    private string ResolveRequestedPrivacy(string? requested, TikTokCreatorInfo creatorInfo)
    {
        var allowed = creatorInfo.PrivacyLevelOptions;
        if (allowed.Count == 0)
        {
            // TikTok returned no options at all (unexpected) - fall back to our
            // own preference rather than blocking the publish outright.
            return ResolvePrivacyLevel();
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (allowed.Contains(requested, StringComparer.OrdinalIgnoreCase))
            {
                return requested;
            }

            throw new PublishException(
                $"TikTok không cho phép chế độ hiển thị '{PrivacyLabel(requested)}' cho tài khoản này. " +
                $"Các chế độ khả dụng: {string.Join(", ", allowed.Select(PrivacyLabel))}. Hãy chọn lại rồi thử lại.",
                retryable: false);
        }

        var preferred = ResolvePrivacyLevel();
        return allowed.FirstOrDefault(a => string.Equals(a, preferred, StringComparison.OrdinalIgnoreCase))
            ?? allowed.FirstOrDefault(a => string.Equals(a, "SELF_ONLY", StringComparison.OrdinalIgnoreCase))
            ?? allowed[0];
    }

    private async Task<OAuthTokens> TokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsync(
            $"{_options.ApiBaseUrl}/v2/oauth/token/", new FormUrlEncodedContent(form), cancellationToken);
        var json = await ReadJsonAsync(resp, cancellationToken);

        if (!resp.IsSuccessStatusCode || json.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(err.GetString()))
        {
            // Only a transient failure on TikTok's side (5xx/429/408) is retryable; a
            // 400 invalid_grant (revoked/expired refresh token) is permanent and must
            // not cause the caller to keep retrying against a dead refresh token.
            var retryable = (int)resp.StatusCode >= 500
                || resp.StatusCode == HttpStatusCode.TooManyRequests
                || resp.StatusCode == HttpStatusCode.RequestTimeout;
            throw new PublishException($"TikTok OAuth thất bại: {json.RootElement}", retryable);
        }

        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 0;
        var scope = root.TryGetProperty("scope", out var s) ? s.GetString() : null;
        var openId = root.TryGetProperty("open_id", out var o) ? o.GetString() : null;

        return new OAuthTokens(
            access, refresh,
            expiresIn > 0 ? DateTimeOffset.UtcNow.AddSeconds(expiresIn) : null,
            scope, openId, openId);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (JsonException)
        {
            throw new PublishException($"TikTok trả về phản hồi không phải JSON ({(int)resp.StatusCode}): {Trim(text)}", retryable: true);
        }
    }

    private void ThrowOnTikTokError(HttpResponseMessage resp, JsonDocument json, string phase, string? sentPrivacyLevel = null)
    {
        // TikTok wraps everything in { data, error: { code, message } }. code "ok" = success.
        if (json.RootElement.TryGetProperty("error", out var error) &&
            error.TryGetProperty("code", out var code) &&
            code.GetString() is { } c && !string.Equals(c, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : c;
            if (string.Equals(c, "unaudited_client_can_only_post_to_private_accounts", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "TikTok {Phase} rejected with unaudited_client_can_only_post_to_private_accounts. " +
                    "We sent privacy_level={SentPrivacy} (raw TikTok message: {Message})",
                    phase, sentPrivacyLevel ?? "(n/a)", message);
                // We already send whatever privacy_level_options/SELF_ONLY told us to
                // send, so this means TikTok is enforcing the deeper, account-level
                // restriction: an unaudited app can only post at all when the
                // connected TikTok *account itself* is set to Private - post-level
                // SELF_ONLY is not sufficient on its own. TikTok's own restriction can
                // also take time to apply after the account is switched, or can require
                // the account to be registered as a target/test user for this app in
                // the TikTok Developer Portal while it's unaudited - explain all three
                // instead of just repeating "make it private" to someone who already did.
                throw new PublishException(
                    $"TikTok từ chối đăng (đã gửi privacy_level={sentPrivacyLevel ?? "SELF_ONLY"}) vì app này chưa được TikTok audit " +
                    "và chỉ được đăng lên tài khoản Riêng tư (Private). Nếu tài khoản TikTok đang kết nối ĐÃ ở chế độ Riêng tư mà vẫn gặp lỗi này: " +
                    "(1) đợi vài phút để TikTok đồng bộ thay đổi rồi thử lại, (2) kiểm tra đúng tài khoản đang kết nối (xem tên hiển thị ở Bước 7), " +
                    "(3) ngắt kết nối và kết nối lại TikTok để làm mới phiên đăng nhập, (4) trong chế độ chưa audit, TikTok có thể yêu cầu tài khoản " +
                    "này phải được thêm làm 'target user' trong TikTok Developer Portal của app - việc này nằm ngoài phạm vi ứng dụng, cần cấu hình phía TikTok.",
                    retryable: false);
            }
            var retryable = c is "rate_limit_exceeded" or "internal_error" || (int)resp.StatusCode >= 500;
            throw new PublishException($"TikTok {phase} lỗi: {message} ({c})", retryable);
        }

        if (!resp.IsSuccessStatusCode)
        {
            throw new PublishException($"TikTok {phase} HTTP {(int)resp.StatusCode}", retryable: (int)resp.StatusCode >= 500);
        }
    }

    private static string ToQuery(Dictionary<string, string?> parts) =>
        string.Join("&", parts.Where(p => p.Value is not null).Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
    private static string Trim2200(string s) => s.Length <= 2200 ? s : s[..2200];
}
