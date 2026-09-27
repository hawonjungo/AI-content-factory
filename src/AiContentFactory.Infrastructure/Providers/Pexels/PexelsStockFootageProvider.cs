using System.Text.Json;
using AiContentFactory.Application.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Providers.Pexels;

public class PexelsOptions
{
    public const string SectionName = "Stock:Pexels";

    /// <summary>Free key from https://www.pexels.com/api/ - blank = stock footage is unavailable.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.pexels.com";

    public long MaxDownloadBytes { get; set; } = 150L * 1024 * 1024;

    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Free stock video from Pexels (https://www.pexels.com/api/). Results are
/// requested portrait-first. Downloads only ever fetch a link the Pexels API
/// itself returned for that video id, over HTTPS on a pexels.com host, and
/// are streamed to a temp file with a size cap - never a caller-supplied URL.
/// </summary>
public class PexelsStockFootageProvider : IStockFootageProvider
{
    private const int PerPage = 12;
    private const int TargetHeight = 1920;

    private readonly HttpClient _httpClient;
    private readonly PexelsOptions _options;
    private readonly ILogger<PexelsStockFootageProvider> _logger;

    public PexelsStockFootageProvider(HttpClient httpClient, IOptions<PexelsOptions> options, ILogger<PexelsStockFootageProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<StockVideo>> SearchAsync(string query, int page, CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/videos/search?query={Uri.EscapeDataString(query)}&orientation=portrait&size=medium&per_page={PerPage}&page={Math.Max(1, page)}";
        using var doc = await GetJsonAsync(url, cancellationToken);

        var results = new List<StockVideo>();
        if (!doc.RootElement.TryGetProperty("videos", out var videos))
        {
            return results;
        }

        foreach (var video in videos.EnumerateArray())
        {
            var user = video.TryGetProperty("user", out var u) ? u : default;
            results.Add(new StockVideo(
                video.GetProperty("id").GetRawText(),
                StringOf(video, "image"),
                video.TryGetProperty("duration", out var d) && d.TryGetDouble(out var seconds) ? seconds : 0,
                IntOf(video, "width"),
                IntOf(video, "height"),
                StringOf(video, "url"),
                user.ValueKind == JsonValueKind.Object ? StringOf(user, "name") : "Pexels",
                user.ValueKind == JsonValueKind.Object ? StringOf(user, "url") : "https://www.pexels.com"));
        }

        return results;
    }

    public async Task<StockDownload> DownloadAsync(string videoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoId) || !videoId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Invalid Pexels video id.", nameof(videoId));
        }

        using var doc = await GetJsonAsync($"{_options.BaseUrl.TrimEnd('/')}/videos/videos/{videoId}", cancellationToken);
        var file = PickFile(doc.RootElement)
            ?? throw new InvalidOperationException("Video này không có bản MP4 phù hợp để tải.");

        if (!Uri.TryCreate(file.Link, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps ||
            !(link.Host.Equals("pexels.com", StringComparison.OrdinalIgnoreCase) || link.Host.EndsWith(".pexels.com", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Refusing a Pexels download link outside pexels.com: {Host}", link?.Host);
            throw new InvalidOperationException("Link tải video không hợp lệ.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"acf-pexels-{videoId}-{Guid.NewGuid():N}.mp4");
        var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using var response = await _httpClient.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declared && declared > _options.MaxDownloadBytes)
            {
                throw new InvalidOperationException("Video stock quá lớn để tải.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > _options.MaxDownloadBytes)
                {
                    throw new InvalidOperationException("Video stock quá lớn để tải.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            output.Position = 0;
            return new StockDownload(output, $"pexels-{videoId}.mp4", file.Width, file.Height);
        }
        catch
        {
            await output.DisposeAsync();
            throw;
        }
    }

    private sealed record FileChoice(string Link, int Width, int Height);

    /// <summary>
    /// The MP4 rendition to import: portrait first, then the one whose height
    /// is closest to 1920 without exceeding it (else the smallest above it).
    /// </summary>
    private static FileChoice? PickFile(JsonElement video)
    {
        if (!video.TryGetProperty("video_files", out var files))
        {
            return null;
        }

        var candidates = files.EnumerateArray()
            .Where(f => StringOf(f, "file_type").Equals("video/mp4", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(StringOf(f, "link")))
            .Select(f => new FileChoice(StringOf(f, "link"), IntOf(f, "width"), IntOf(f, "height")))
            .ToList();

        return candidates
            .OrderByDescending(f => f.Height > f.Width)
            .ThenBy(f => f.Height <= TargetHeight ? 0 : 1)
            .ThenBy(f => Math.Abs(TargetHeight - f.Height))
            .FirstOrDefault();
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Chưa có PEXELS_API_KEY - lấy key miễn phí tại https://www.pexels.com/api/.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", _options.ApiKey); // Pexels: the bare key, no scheme word
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Pexels returned {Status}", (int)response.StatusCode);
            throw new InvalidOperationException((int)response.StatusCode == 429
                ? "Pexels đang giới hạn số lần gọi - thử lại sau ít phút."
                : $"Pexels báo lỗi {(int)response.StatusCode}.");
        }

        return JsonDocument.Parse(body);
    }

    private static string StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static int IntOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
}
