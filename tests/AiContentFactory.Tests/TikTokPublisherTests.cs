using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;
using AiContentFactory.Infrastructure.Providers.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class TikTokPublisherTests
{
    /// <summary>Routes by request path so a test can script creator_info/init/status without a real TikTok call.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? OnCreatorInfo { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnInit { get; set; }
        public List<string> RequestedPaths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            RequestedPaths.Add(path);

            if (path.Contains("creator_info") && OnCreatorInfo is not null)
            {
                return Task.FromResult(OnCreatorInfo(request));
            }
            if (path.Contains("video/init") && OnInit is not null)
            {
                return Task.FromResult(OnInit(request));
            }

            throw new InvalidOperationException($"Unexpected request to {path}");
        }
    }

    private static HttpResponseMessage JsonOk(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static TikTokPublisher Build(FakeHandler handler, bool isAudited = false) =>
        new(new HttpClient(handler),
            Options.Create(new TikTokPublishOptions { ClientId = "id", ClientSecret = "secret", IsAudited = isAudited }),
            NullLogger<TikTokPublisher>.Instance);

    private static object CreatorInfoBody(params string[] privacyLevelOptions) => new
    {
        data = new
        {
            creator_username = "tester",
            privacy_level_options = privacyLevelOptions
        },
        error = new { code = "ok", message = "", log_id = "1" }
    };

    [Fact]
    public async Task Unaudited_app_gets_SELF_ONLY_as_the_default_and_a_notice()
    {
        var handler = new FakeHandler { OnCreatorInfo = _ => JsonOk(CreatorInfoBody("MUTUAL_FOLLOW_FRIENDS", "FOLLOWER_OF_CREATOR", "SELF_ONLY")) };
        var publisher = Build(handler, isAudited: false);

        var options = await publisher.GetPublishOptionsAsync("token");

        Assert.Equal(3, options.PrivacyOptions.Count);
        Assert.Equal("SELF_ONLY", options.DefaultPrivacy);
        Assert.Contains("audit", options.Notice, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(options.PrivacyOptions, o => o.Value == "PUBLIC_TO_EVERYONE");
    }

    [Fact]
    public async Task Audited_app_with_public_option_gets_no_restriction_notice()
    {
        var handler = new FakeHandler { OnCreatorInfo = _ => JsonOk(CreatorInfoBody("PUBLIC_TO_EVERYONE", "SELF_ONLY")) };
        var publisher = Build(handler, isAudited: true);

        var options = await publisher.GetPublishOptionsAsync("token");

        Assert.Equal("PUBLIC_TO_EVERYONE", options.DefaultPrivacy);
        Assert.Null(options.Notice);
    }

    [Fact]
    public async Task Upload_uses_the_only_TikTok_allowed_privacy_when_none_was_requested()
    {
        string? sentPrivacy = null;
        var handler = new FakeHandler
        {
            OnCreatorInfo = _ => JsonOk(CreatorInfoBody("SELF_ONLY")),
            OnInit = req =>
            {
                var body = req.Content!.ReadFromJsonAsync<JsonElement>().GetAwaiter().GetResult();
                sentPrivacy = body.GetProperty("post_info").GetProperty("privacy_level").GetString();
                // upload_url deliberately null - enough to make UploadAsync throw
                // right after init, proving what privacy_level it sent without
                // needing to script the full upload/poll round trip.
                return JsonOk(new
                {
                    data = new { publish_id = "pub1", upload_url = (string?)null },
                    error = new { code = "ok", message = "", log_id = "1" }
                });
            }
        };
        var publisher = Build(handler);

        await Assert.ThrowsAsync<PublishException>(() => publisher.UploadAsync(MakeRequest(privacy: null)));

        Assert.Equal("SELF_ONLY", sentPrivacy);
    }

    [Fact]
    public async Task Requesting_an_unavailable_privacy_level_fails_clearly_without_calling_init()
    {
        var handler = new FakeHandler { OnCreatorInfo = _ => JsonOk(CreatorInfoBody("SELF_ONLY")) };
        var publisher = Build(handler);

        var ex = await Assert.ThrowsAsync<PublishException>(() => publisher.UploadAsync(MakeRequest(privacy: "PUBLIC_TO_EVERYONE")));

        Assert.False(ex.Retryable);
        Assert.DoesNotContain(handler.RequestedPaths, p => p.Contains("video/init"));
    }

    [Fact]
    public async Task Unaudited_client_error_from_init_explains_the_account_privacy_requirement()
    {
        var handler = new FakeHandler
        {
            OnCreatorInfo = _ => JsonOk(CreatorInfoBody("SELF_ONLY")),
            OnInit = _ => JsonOk(new
            {
                data = new { },
                error = new
                {
                    code = "unaudited_client_can_only_post_to_private_accounts",
                    message = "Please review our integration guidelines at https://developers.tiktok.com/doc/content-sharing-guidelines/",
                    log_id = "1"
                }
            })
        };
        var publisher = Build(handler);

        var ex = await Assert.ThrowsAsync<PublishException>(() => publisher.UploadAsync(MakeRequest(privacy: null)));

        Assert.False(ex.Retryable);
        Assert.Contains("Riêng tư", ex.Message);
        Assert.Contains("audit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PublishUploadRequest MakeRequest(string? privacy) => new(
        new PublishMetadata("Title", "Description"),
        new PublishVideo(_ => Task.FromResult<Stream>(new MemoryStream(new byte[10])), 10, MediaInfo.Unreadable("n/a"), null),
        "access-token",
        null,
        null,
        privacy);
}
