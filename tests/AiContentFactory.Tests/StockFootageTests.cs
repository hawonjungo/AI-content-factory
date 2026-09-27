using System.Net;
using System.Text;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Infrastructure.Providers.Pexels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Free stock footage (Pexels): search is keyed, downloads only follow links
/// the Pexels API returned on a pexels.com HTTPS host, the best vertical MP4
/// is chosen, and an imported stock clip books no Flow credits. No real
/// network: HTTP is a fake handler.
/// </summary>
public class StockFootageTests
{
    private const string SearchJson = """
        {"videos":[{"id":123,"width":1080,"height":1920,"duration":12,"url":"https://www.pexels.com/video/123/",
          "image":"https://images.pexels.com/videos/123/pic.jpg","user":{"name":"Ana","url":"https://www.pexels.com/@ana"}}]}
        """;

    private static string VideoJson(string link) => $$"""
        {"id":123,"video_files":[
          {"file_type":"video/mp4","width":1920,"height":1080,"link":"https://videos.pexels.com/landscape.mp4"},
          {"file_type":"video/mp4","width":2160,"height":3840,"link":"https://videos.pexels.com/huge.mp4"},
          {"file_type":"video/mp4","width":1080,"height":1920,"link":"{{link}}"},
          {"file_type":"video/webm","width":1080,"height":1920,"link":"https://videos.pexels.com/x.webm"}]}
        """;

    private sealed class Handler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public string VideoLink { get; set; } = "https://videos.pexels.com/portrait-1080.mp4";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            string body = path switch
            {
                "/videos/search" => SearchJson,
                "/videos/videos/123" => VideoJson(VideoLink),
                _ => "MP4BYTES",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }

    private static (PexelsStockFootageProvider Provider, Handler Handler) Pexels(string apiKey = "test-key")
    {
        var handler = new Handler();
        return (new PexelsStockFootageProvider(new HttpClient(handler), Options.Create(new PexelsOptions { ApiKey = apiKey }), NullLogger<PexelsStockFootageProvider>.Instance), handler);
    }

    [Fact]
    public async Task Search_is_portrait_first_sends_the_key_and_maps_attribution()
    {
        var (provider, handler) = Pexels();

        var results = await provider.SearchAsync("egypt temple", 1);

        var request = handler.Requests.Single();
        Assert.Contains("orientation=portrait", request.RequestUri!.Query);
        Assert.Contains("query=egypt%20temple", request.RequestUri.Query);
        Assert.Equal("test-key", request.Headers.GetValues("Authorization").Single());
        var video = Assert.Single(results);
        Assert.Equal("123", video.Id);
        Assert.Equal("Ana", video.Author);
        Assert.Equal(12, video.DurationSeconds);
    }

    [Fact]
    public async Task Download_picks_the_1080x1920_mp4_rendition()
    {
        var (provider, handler) = Pexels();

        await using var download = await provider.DownloadAsync("123");

        Assert.Equal("https://videos.pexels.com/portrait-1080.mp4", handler.Requests.Last().RequestUri!.ToString());
        Assert.Equal(1920, download.Height);
        using var reader = new StreamReader(download.Content);
        Assert.Equal("MP4BYTES", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData("http://videos.pexels.com/a.mp4")]
    [InlineData("https://evil.example.com/a.mp4")]
    [InlineData("https://pexels.com.evil.example/a.mp4")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public async Task A_download_link_outside_pexels_https_is_refused(string link)
    {
        var (provider, handler) = Pexels();
        handler.VideoLink = link;

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DownloadAsync("123"));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.ToString() == link);
    }

    [Theory]
    [InlineData("../123")]
    [InlineData("123?x=1")]
    [InlineData("")]
    public async Task A_non_numeric_video_id_is_refused_before_any_request(string id)
    {
        var (provider, handler) = Pexels();

        await Assert.ThrowsAsync<ArgumentException>(() => provider.DownloadAsync(id));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_called()
    {
        var (provider, handler) = Pexels(apiKey: "");

        Assert.False(provider.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SearchAsync("x", 1));
        Assert.Empty(handler.Requests);
    }

    // ---- Service: import goes through the external (no Flow credits) path ----

    private sealed class RecordingImport : IFlowClipImportService
    {
        public string? Provider { get; private set; }
        public bool FlowImportCalled { get; private set; }

        public Task<FlowClipImportResult> ImportAsync(Guid c, Guid s, string f, Stream content, CancellationToken ct = default)
        {
            FlowImportCalled = true;
            throw new InvalidOperationException("Stock clips must never go through the Flow (credit-booking) import.");
        }

        public Task<FlowClipImportResult> ImportExternalAsync(Guid c, Guid s, string f, Stream content, string provider, CancellationToken ct = default)
        {
            Provider = provider;
            return Task.FromResult(new FlowClipImportResult(s, 1, true, 12, 1080, 1920, "9:16", Array.Empty<string>(), Array.Empty<string>()));
        }

        public Task<FlowImportStatus> GetStatusAsync(Guid c, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StoryboardResponse> SetSkipGenerationAsync(Guid c, Guid s, bool skip, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Importing_a_stock_video_books_no_flow_credits()
    {
        var (provider, _) = Pexels();
        var import = new RecordingImport();
        var service = new StockFootageService(provider, import, NullLogger<StockFootageService>.Instance);

        var result = await service.ImportAsync(Guid.NewGuid(), Guid.NewGuid(), "123");

        Assert.True(result.Accepted);
        Assert.Equal(StockFootageService.Provider, import.Provider);
        Assert.False(import.FlowImportCalled);
    }

    [Fact]
    public async Task A_blank_query_or_missing_key_is_a_clear_message()
    {
        var (configured, _) = Pexels();
        var service = new StockFootageService(configured, new RecordingImport(), NullLogger<StockFootageService>.Instance);
        await Assert.ThrowsAsync<DomainException>(() => service.SearchAsync("  ", 1));

        var (unconfigured, _) = Pexels(apiKey: "");
        var noKey = new StockFootageService(unconfigured, new RecordingImport(), NullLogger<StockFootageService>.Instance);
        var ex = await Assert.ThrowsAsync<DomainException>(() => noKey.SearchAsync("temple", 1));
        Assert.Contains("PEXELS_API_KEY", ex.Message);
    }
}
