using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// Free stock footage for scenes that need no recurring character (places,
/// crowds, objects, b-roll): search, then import a chosen video as the
/// scene's clip through the same validation as a Google Flow clip - no AI
/// call, no Flow credits.
/// </summary>
public interface IStockFootageService
{
    Task<IReadOnlyList<StockVideo>> SearchAsync(string query, int page, CancellationToken cancellationToken = default);

    Task<FlowClipImportResult> ImportAsync(Guid contentProjectId, Guid sceneId, string videoId, CancellationToken cancellationToken = default);
}

public class StockFootageService : IStockFootageService
{
    public const string Provider = "pexels";
    private const int MaxQueryLength = 100;

    private readonly IStockFootageProvider _provider;
    private readonly IFlowClipImportService _clipImport;
    private readonly ILogger<StockFootageService> _logger;

    public StockFootageService(IStockFootageProvider provider, IFlowClipImportService clipImport, ILogger<StockFootageService> logger)
    {
        _provider = provider;
        _clipImport = clipImport;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StockVideo>> SearchAsync(string query, int page, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainException("Nhập từ khóa tìm video (tiếng Anh cho kết quả tốt nhất).");
        }

        if (trimmed.Length > MaxQueryLength)
        {
            trimmed = trimmed[..MaxQueryLength];
        }

        try
        {
            return await _provider.SearchAsync(trimmed, page, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Stock footage search failed for '{Query}'", trimmed);
            throw new DomainException(ex is InvalidOperationException ? ex.Message : "Không tìm được video stock - thử lại sau.");
        }
    }

    public async Task<FlowClipImportResult> ImportAsync(Guid contentProjectId, Guid sceneId, string videoId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(videoId) || !videoId.All(char.IsAsciiDigit))
        {
            throw new DomainException("Mã video stock không hợp lệ.");
        }

        StockDownload download;
        try
        {
            download = await _provider.DownloadAsync(videoId, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or ArgumentException)
        {
            _logger.LogWarning(ex, "Stock footage download failed for video {VideoId}", videoId);
            throw new DomainException(ex is InvalidOperationException ? ex.Message : "Không tải được video stock - thử lại sau.");
        }

        await using (download)
        {
            var result = await _clipImport.ImportExternalAsync(contentProjectId, sceneId, download.FileName, download.Content, Provider, cancellationToken);
            _logger.LogInformation(
                "Stock video {VideoId} imported for Scene {SceneId} (accepted: {Accepted})", videoId, sceneId, result.Accepted);
            return result;
        }
    }

    private void EnsureConfigured()
    {
        if (!_provider.IsConfigured)
        {
            throw new DomainException("Chưa có PEXELS_API_KEY - lấy key miễn phí tại https://www.pexels.com/api/ rồi đặt vào file .env.");
        }
    }
}
