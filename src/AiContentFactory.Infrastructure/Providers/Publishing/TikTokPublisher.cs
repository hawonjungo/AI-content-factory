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

    public async Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default)
    {
        var size = request.Video.SizeBytes;

        // 1. init
        var initBody = new
        {
            post_info = new
            {
                title = Trim2200($"{request.Metadata.Title}\n{request.Metadata.Description}".Trim()),
                privacy_level = "PUBLIC_TO_EVERYONE",
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
        ThrowOnTikTokError(initResp, initJson, "init");

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

    private async Task<OAuthTokens> TokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsync(
            $"{_options.ApiBaseUrl}/v2/oauth/token/", new FormUrlEncodedContent(form), cancellationToken);
        var json = await ReadJsonAsync(resp, cancellationToken);

        if (!resp.IsSuccessStatusCode || json.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(err.GetString()))
        {
            throw new PublishException($"TikTok OAuth thất bại: {json.RootElement}", retryable: false);
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

    private static void ThrowOnTikTokError(HttpResponseMessage resp, JsonDocument json, string phase)
    {
        // TikTok wraps everything in { data, error: { code, message } }. code "ok" = success.
        if (json.RootElement.TryGetProperty("error", out var error) &&
            error.TryGetProperty("code", out var code) &&
            code.GetString() is { } c && !string.Equals(c, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var message = error.TryGetProperty("message", out var m) ? m.GetString() : c;
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
