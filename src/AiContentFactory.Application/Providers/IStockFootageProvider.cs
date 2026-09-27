namespace AiContentFactory.Application.Providers;

/// <summary>One search hit from a free stock-footage library.</summary>
/// <param name="Id">The library's own video id (digits only for Pexels).</param>
/// <param name="PageUrl">The video's page on the library site (attribution link).</param>
public record StockVideo(
    string Id,
    string PreviewImageUrl,
    double DurationSeconds,
    int Width,
    int Height,
    string PageUrl,
    string Author,
    string AuthorUrl);

/// <param name="Content">A readable stream of the downloaded MP4; the caller disposes it.</param>
public sealed record StockDownload(Stream Content, string FileName, int Width, int Height) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// The replaceable seam for free stock footage (Pexels today). Searching and
/// downloading are free; the provider needs its own (free) API key.
/// </summary>
public interface IStockFootageProvider
{
    bool IsConfigured { get; }

    /// <summary>Portrait-oriented results first; empty when nothing matches.</summary>
    Task<IReadOnlyList<StockVideo>> SearchAsync(string query, int page, CancellationToken cancellationToken = default);

    /// <summary>Downloads the best vertical MP4 rendition of <paramref name="videoId"/> (capped in size).</summary>
    Task<StockDownload> DownloadAsync(string videoId, CancellationToken cancellationToken = default);
}
