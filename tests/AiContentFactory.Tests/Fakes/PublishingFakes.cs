using AiContentFactory.Application.Publishing;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Domain.Publishing;

namespace AiContentFactory.Tests.Fakes;

public sealed class FakeTokenProtector : ITokenProtector
{
    public string Protect(string plaintext) => "enc:" + plaintext;
    public string Unprotect(string ciphertext) => ciphertext.StartsWith("enc:") ? ciphertext[4..] : ciphertext;
}

public sealed class FakePublishJobScheduler : IPublishJobScheduler
{
    public List<Guid> EnqueuedNow { get; } = new();
    public List<(Guid Id, DateTimeOffset At)> Scheduled { get; } = new();

    public void EnqueueNow(Guid publishJobId) => EnqueuedNow.Add(publishJobId);
    public void Schedule(Guid publishJobId, DateTimeOffset runAtUtc) => Scheduled.Add((publishJobId, runAtUtc));
}

public sealed class FakePublishJobRepository : IPublishJobRepository
{
    public List<PublishJob> Jobs { get; } = new();

    public Task<PublishJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Jobs.FirstOrDefault(j => j.Id == id));

    public Task<IReadOnlyList<PublishJob>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PublishJob>>(Jobs.Where(j => j.ContentProjectId == contentProjectId).ToList());

    public Task<PublishJob?> GetActiveAsync(Guid contentProjectId, PublishTarget platform, CancellationToken cancellationToken = default) =>
        Task.FromResult(Jobs.FirstOrDefault(j => j.ContentProjectId == contentProjectId && j.Platform == platform && j.IsActive));

    public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default)
    {
        Jobs.Add(job);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeSocialConnectionService : ISocialConnectionService
{
    private readonly HashSet<PublishTarget> _connected = new();

    public void Connect(PublishTarget platform) => _connected.Add(platform);

    public Task<IReadOnlyList<SocialConnectionDto>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SocialConnectionDto>>(Enum.GetValues<PublishTarget>()
            .Select(p => new SocialConnectionDto(p.ToString(), _connected.Contains(p) ? "Connected" : "Disconnected", "acct", true, null, _connected.Contains(p) ? "acct-" + p : null))
            .ToList());

    public Task<string> BuildAuthorizationUrlAsync(PublishTarget platform, string? redirectUriOverride, CancellationToken cancellationToken = default) =>
        Task.FromResult($"https://auth.example/{platform}");

    public Task<SocialConnectionDto> CompleteAsync(PublishTarget platform, string code, string? redirectUriOverride, CancellationToken cancellationToken = default)
    {
        _connected.Add(platform);
        return Task.FromResult(new SocialConnectionDto(platform.ToString(), "Connected", "acct", true, null, "acct-" + platform));
    }

    public Task<SocialConnectionDto> SelectPageAsync(PublishTarget platform, string pageId, CancellationToken cancellationToken = default)
    {
        _connected.Add(platform);
        return Task.FromResult(new SocialConnectionDto(platform.ToString(), "Connected", pageId, true, null, pageId));
    }

    public Task DisconnectAsync(PublishTarget platform, CancellationToken cancellationToken = default)
    {
        _connected.Remove(platform);
        return Task.CompletedTask;
    }

    public Task<UsableAccessToken> GetUsableAccessTokenAsync(PublishTarget platform, CancellationToken cancellationToken = default) =>
        _connected.Contains(platform)
            ? Task.FromResult(new UsableAccessToken("token-" + platform, "acct-" + platform))
            : throw new PublishException($"{platform} chưa kết nối.", retryable: false);
}

public sealed class FakeSocialConnectionRepository : ISocialConnectionRepository
{
    public List<SocialConnection> Rows { get; } = new();

    public Task<SocialConnection?> GetAsync(PublishTarget platform, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(c => c.Platform == platform));

    public Task<IReadOnlyList<SocialConnection>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SocialConnection>>(Rows.ToList());

    public Task AddAsync(SocialConnection connection, CancellationToken cancellationToken = default)
    {
        Rows.Add(connection);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Configurable publisher: toggle IsConfigured, per-call video constraint, and the upload outcome.</summary>
public sealed class FakeSocialPlatformPublisher : ISocialPlatformPublisher
{
    public FakeSocialPlatformPublisher(PublishTarget platform) => Platform = platform;

    public PublishTarget Platform { get; }
    public bool IsConfigured { get; set; } = true;
    public Func<MediaInfo, long, VideoConstraintResult> Constraint { get; set; } = (_, _) => VideoConstraintResult.Valid;

    /// <summary>Return a result, or throw (PublishException to control retryability).</summary>
    public Func<PublishUploadRequest, PublishResult> Upload { get; set; } =
        _ => new PublishResult("ext-id", "https://example.com/post/ext-id");

    public int UploadCalls { get; private set; }
    public int RefreshCalls { get; private set; }

    /// <summary>When set, ExchangeCodeAsync returns these as multi-target candidates (Facebook Pages).</summary>
    public IReadOnlyList<OAuthAccountOption>? Accounts { get; set; }

    public string GetAuthorizationUrl(string redirectUri, string state) => $"https://auth.example/{Platform}?state={state}";

    public Task<OAuthTokens> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) =>
        Task.FromResult(new OAuthTokens("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), "scope", "acct", "Account", Accounts));

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        return Task.FromResult(new OAuthTokens("access2", "refresh", DateTimeOffset.UtcNow.AddHours(1), "scope", "acct", "Account"));
    }

    public VideoConstraintResult ValidateVideo(MediaInfo probe, long sizeBytes) => Constraint(probe, sizeBytes);

    public Task<PublishResult> UploadAsync(PublishUploadRequest request, CancellationToken cancellationToken = default)
    {
        UploadCalls++;
        return Task.FromResult(Upload(request));
    }
}
