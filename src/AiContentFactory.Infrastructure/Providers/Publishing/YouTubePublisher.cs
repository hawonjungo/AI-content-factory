using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Publishing;

/// <summary>
/// YouTube Data API v3 resumable upload (videos.insert). Flow:
///   1. OAuth: accounts.google.com authorize (scope youtube.upload, access_type=offline)
///      -> code -> POST oauth2.googleapis.com/token.
///   2. POST /videos?uploadType=resumable&amp;part=snippet,status with the metadata JSON
///      -> "Location" header = the resumable session URI.
///   3. PUT the MP4 bytes to that URI (Content-Type video/mp4). 200/201 body has { id }.
/// A scheduled publish uses status.privacyStatus="private" + status.publishAt (RFC3339);
/// otherwise the configured privacyStatus. "#Shorts" is appended to the description so
/// YouTube classifies the vertical clip as a Short.
/// Docs: https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol
/// </summary>
public class YouTubePublisher : ISocialPlatformPublisher
{
    // Shorts must be vertical/square and <= 3 minutes.
    private const double MaxShortsSeconds = 180;

    private readonly HttpClient _http;
    private readonly YouTubePublishOptions _options;
    private readonly ILogger<YouTubePublisher> _logger;

    public YouTubePublisher(HttpClient http, IOptions<YouTubePublishOptions> options, ILogger<YouTubePublisher> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public PublishTarget Platform => PublishTarget.YouTubeShorts;
    public bool IsConfigured => _options.IsConfigured;
    public string RequiredScope => "https://www.googleapis.com/auth/youtube.upload";

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var scope = string.IsNullOrWhiteSpace(_options.Scope)
            ? "https://www.googleapis.com/auth/youtube.upload"
            : _options.Scope;
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["access_type"] = "offline",
            ["include_granted_scopes"] = "true",
            ["prompt"] = "consent",
            ["state"] = state
        };
        return $"{_options.OAuthAuthorizeUrl}?{ToQuery(query)}";
    }

    public Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) =>
        TokenRequestAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        TokenRequestAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);

    public VideoConstraintResult ValidateVideo(MediaInfo probe, long sizeBytes)
    {
        if (probe.DurationSeconds > MaxShortsSeconds)
        {
            return VideoConstraintResult.Invalid(
                $"YouTube Shorts: clip dài {probe.DurationSeconds:0}s, tối đa {MaxShortsSeconds:0}s.");
        }
        return VideoConstraintResult.Valid;
    }

    private static readonly IReadOnlyList<PublishPrivacyOption> VisibilityOptions = new[]
    {
        new PublishPrivacyOption("public", "Công khai"),
        new PublishPrivacyOption("unlisted", "Không công khai (Unlisted)"),
        new PublishPrivacyOption("private", "Riêng tư"),
    };

    /// <summary>
    /// YouTube's visibility set is fixed by the Data API itself (no per-creator
    /// query needed, unlike TikTok) - always public/unlisted/private.
    /// </summary>
    public Task<PlatformPublishOptions> GetPublishOptionsAsync(string accessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PlatformPublishOptions(VisibilityOptions, _options.PrivacyStatus, null));

    public async Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default)
    {
        var scheduled = request.PublishAtUtc is { } at && at > DateTimeOffset.UtcNow;
        var description = AppendShorts(request.Metadata.Description);
        var visibility = VisibilityOptions.Any(o => string.Equals(o.Value, request.Privacy, StringComparison.OrdinalIgnoreCase))
            ? request.Privacy!
            : _options.PrivacyStatus;

        var metadata = new
        {
            snippet = new
            {
                title = Trim(request.Metadata.Title, 100),
                description = Trim(description, 5000),
                tags = ExtractTags(description),
                categoryId = _options.CategoryId
            },
            status = new
            {
                // A scheduled upload must stay "private" until publishAt regardless
                // of the requested visibility - YouTube publishes it automatically then.
                privacyStatus = scheduled ? "private" : visibility,
                publishAt = scheduled ? request.PublishAtUtc!.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ") : null,
                selfDeclaredMadeForKids = false
            }
        };

        var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        // 1. start resumable session
        using var startReq = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.UploadBaseUrl}/videos?uploadType=resumable&part=snippet,status")
        {
            Content = new StringContent(metaJson, Encoding.UTF8, "application/json")
        };
        startReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);
        startReq.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "video/mp4");
        startReq.Headers.TryAddWithoutValidation("X-Upload-Content-Length", request.Video.SizeBytes.ToString());

        using var startResp = await _http.SendAsync(startReq, cancellationToken);
        if (!startResp.IsSuccessStatusCode)
        {
            var body = await startResp.Content.ReadAsStringAsync(cancellationToken);
            throw MapHttpError(startResp.StatusCode, body, "khởi tạo phiên upload");
        }

        var sessionUri = startResp.Headers.Location?.ToString()
            ?? throw new PublishException("YouTube không trả về resumable session URI.", retryable: true);

        // 2. PUT the bytes
        await using var stream = await request.Video.OpenStream(cancellationToken);
        using var putReq = new HttpRequestMessage(HttpMethod.Put, sessionUri) { Content = new StreamContent(stream) };
        putReq.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        putReq.Content.Headers.ContentLength = request.Video.SizeBytes;

        using var putResp = await _http.SendAsync(putReq, cancellationToken);
        var putBody = await putResp.Content.ReadAsStringAsync(cancellationToken);
        if (!putResp.IsSuccessStatusCode)
        {
            throw MapHttpError(putResp.StatusCode, putBody, "upload video");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(putBody) ? "{}" : putBody);
        var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new PublishException($"YouTube upload không trả về video id: {Trim(putBody, 300)}", retryable: true);
        }

        return new PublishResult(id!, $"https://www.youtube.com/shorts/{id}");
    }

    private async Task<OAuthTokens> TokenRequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsync(_options.OAuthTokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);

        if (!resp.IsSuccessStatusCode)
        {
            // Only a transient failure on Google's side (5xx/429/408) is retryable; a
            // 400 invalid_grant (revoked/expired refresh token) is permanent and must
            // not cause the caller to keep retrying against a dead refresh token.
            var retryable = (int)resp.StatusCode >= 500
                || resp.StatusCode == HttpStatusCode.TooManyRequests
                || resp.StatusCode == HttpStatusCode.RequestTimeout;
            throw new PublishException($"YouTube OAuth thất bại: {Trim(text, 300)}", retryable);
        }

        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
        var scope = root.TryGetProperty("scope", out var s) ? s.GetString() : null;

        return new OAuthTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn), scope, null, "YouTube channel");
    }

    private static PublishException MapHttpError(HttpStatusCode status, string body, string phase)
    {
        var reason = TryGetErrorReason(body);
        if (status == HttpStatusCode.Unauthorized && reason == "youtubeSignupRequired")
        {
            return new PublishException(
                "Tài khoản Google này chưa có kênh YouTube. Hãy tạo kênh tại " +
                "https://www.youtube.com/create_channel rồi kết nối lại ở Bước 7.",
                retryable: false);
        }

        var retryable = (int)status >= 500 || status == HttpStatusCode.TooManyRequests || status == HttpStatusCode.RequestTimeout;
        return new PublishException($"YouTube {phase} lỗi HTTP {(int)status}: {Trim(body, 400)}", retryable);
    }

    /// <summary>Reads the first <c>error.errors[].reason</c> from a Google API error body, e.g. "youtubeSignupRequired".</summary>
    private static string? TryGetErrorReason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0 &&
                errors[0].TryGetProperty("reason", out var reasonEl))
            {
                return reasonEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON body; fall through and let the caller use the generic message.
        }
        return null;
    }

    private static string AppendShorts(string description) =>
        description.Contains("#Shorts", StringComparison.OrdinalIgnoreCase)
            ? description
            : string.IsNullOrWhiteSpace(description) ? "#Shorts" : description.TrimEnd() + "\n\n#Shorts";

    private static string[] ExtractTags(string text) =>
        text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.StartsWith('#') && t.Length > 1)
            .Select(t => t.TrimStart('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToArray();

    private static string ToQuery(Dictionary<string, string?> parts) =>
        string.Join("&", parts.Where(p => p.Value is not null).Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
