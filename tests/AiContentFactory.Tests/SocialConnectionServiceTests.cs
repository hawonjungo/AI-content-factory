using AiContentFactory.Application.Publishing;
using AiContentFactory.Domain.Publishing;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class SocialConnectionServiceTests
{
    private sealed class Harness
    {
        public required SocialConnectionService Service { get; init; }
        public required FakeSocialConnectionRepository Repo { get; init; }
        public required FakeSocialPlatformPublisher Facebook { get; init; }
        public required FakeSocialPlatformPublisher Instagram { get; init; }
    }

    private static Harness Build()
    {
        var repo = new FakeSocialConnectionRepository();
        var facebook = new FakeSocialPlatformPublisher(PublishTarget.FacebookPage);
        var instagram = new FakeSocialPlatformPublisher(PublishTarget.InstagramReels);

        var service = new SocialConnectionService(
            repo,
            new ISocialPlatformPublisher[] { facebook, instagram },
            new FakeTokenProtector(),
            Options.Create(new PublishingOptions { PublicBaseUrl = "https://api.test" }),
            NullLogger<SocialConnectionService>.Instance);

        return new Harness { Service = service, Repo = repo, Facebook = facebook, Instagram = instagram };
    }

    private static OAuthAccountOption Page(string id, string name) =>
        new(id, name, $"page-token-{id}", DateTimeOffset.UtcNow.AddDays(50));

    [Fact]
    public void Facebook_slug_and_parse_round_trip()
    {
        Assert.Equal("facebook", PublishTargets.Slug(PublishTarget.FacebookPage));
        Assert.True(PublishTargets.TryParse("facebook", out var p) && p == PublishTarget.FacebookPage);
        Assert.True(PublishTargets.TryParse("FacebookPage", out var p2) && p2 == PublishTarget.FacebookPage);
    }

    [Fact]
    public async Task A_single_Page_is_auto_selected_and_the_connection_becomes_Connected()
    {
        var h = Build();
        h.Facebook.Accounts = new[] { Page("100", "My Page") };

        var dto = await h.Service.CompleteAsync(PublishTarget.FacebookPage, "code", null);

        Assert.Equal(nameof(SocialConnectionStatus.Connected), dto.Status);
        Assert.Equal("100", dto.AccountId);
        Assert.Equal("My Page", dto.AccountName);

        var token = await h.Service.GetUsableAccessTokenAsync(PublishTarget.FacebookPage);
        Assert.Equal("page-token-100", token.AccessToken); // the PAGE token, decrypted
        Assert.Equal("100", token.AccountId);
    }

    [Fact]
    public async Task Multiple_Pages_leave_the_connection_awaiting_selection_and_expose_the_choices()
    {
        var h = Build();
        h.Facebook.Accounts = new[] { Page("1", "Alpha"), Page("2", "Beta") };

        var dto = await h.Service.CompleteAsync(PublishTarget.FacebookPage, "code", null);

        Assert.Equal(nameof(SocialConnectionStatus.PendingSelection), dto.Status);
        Assert.Null(dto.AccountId);
        Assert.NotNull(dto.Pages);
        Assert.Equal(new[] { "1", "2" }, dto.Pages!.Select(p => p.Id).ToArray());
        Assert.Equal(new[] { "Alpha", "Beta" }, dto.Pages!.Select(p => p.Name).ToArray());

        // Not usable for publishing until a Page is picked.
        await Assert.ThrowsAsync<PublishException>(() => h.Service.GetUsableAccessTokenAsync(PublishTarget.FacebookPage));
    }

    [Fact]
    public async Task SelectPage_finalises_the_connection_with_the_chosen_Page_and_its_token()
    {
        var h = Build();
        h.Facebook.Accounts = new[] { Page("1", "Alpha"), Page("2", "Beta") };
        await h.Service.CompleteAsync(PublishTarget.FacebookPage, "code", null);

        var dto = await h.Service.SelectPageAsync(PublishTarget.FacebookPage, "2");

        Assert.Equal(nameof(SocialConnectionStatus.Connected), dto.Status);
        Assert.Equal("2", dto.AccountId);
        Assert.Equal("Beta", dto.AccountName);

        var token = await h.Service.GetUsableAccessTokenAsync(PublishTarget.FacebookPage);
        Assert.Equal("page-token-2", token.AccessToken);
        Assert.Equal("2", token.AccountId);
        Assert.Null(h.Repo.Rows.Single().PendingSelectionData); // cleared
    }

    [Fact]
    public async Task SelectPage_with_an_unknown_id_is_rejected()
    {
        var h = Build();
        h.Facebook.Accounts = new[] { Page("1", "Alpha"), Page("2", "Beta") };
        await h.Service.CompleteAsync(PublishTarget.FacebookPage, "code", null);

        await Assert.ThrowsAsync<PublishException>(() => h.Service.SelectPageAsync(PublishTarget.FacebookPage, "999"));
    }

    [Fact]
    public async Task A_single_account_platform_is_unaffected_by_the_page_selection_branch()
    {
        var h = Build();
        // Instagram publisher returns no Accounts -> the plain single-account Connect path.

        var dto = await h.Service.CompleteAsync(PublishTarget.InstagramReels, "code", null);

        Assert.Equal(nameof(SocialConnectionStatus.Connected), dto.Status);
        Assert.Null(dto.Pages);
        var token = await h.Service.GetUsableAccessTokenAsync(PublishTarget.InstagramReels);
        Assert.Equal("access", token.AccessToken);
    }

    [Fact]
    public async Task A_transient_refresh_failure_does_not_expire_the_connection()
    {
        var h = Build();
        h.Instagram.ExchangeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); // needs refresh immediately
        await h.Service.CompleteAsync(PublishTarget.InstagramReels, "code", null);

        h.Instagram.RefreshOverride = _ => throw new PublishException("Instagram OAuth thất bại: 503", retryable: true);

        var ex = await Assert.ThrowsAsync<PublishException>(() => h.Service.GetUsableAccessTokenAsync(PublishTarget.InstagramReels));
        Assert.True(ex.Retryable);
        Assert.Equal(nameof(SocialConnectionStatus.Connected), h.Repo.Rows.Single().Status.ToString());
    }

    [Fact]
    public async Task A_permanent_refresh_failure_marks_the_connection_Expired()
    {
        var h = Build();
        h.Instagram.ExchangeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); // needs refresh immediately
        await h.Service.CompleteAsync(PublishTarget.InstagramReels, "code", null);

        h.Instagram.RefreshOverride = _ => throw new PublishException("Instagram OAuth thất bại: invalid_grant", retryable: false);

        var ex = await Assert.ThrowsAsync<PublishException>(() => h.Service.GetUsableAccessTokenAsync(PublishTarget.InstagramReels));
        Assert.False(ex.Retryable);
        Assert.Equal(nameof(SocialConnectionStatus.Expired), h.Repo.Rows.Single().Status.ToString());
    }
}
