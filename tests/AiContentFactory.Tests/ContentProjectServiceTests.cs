using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Publishing;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

public class ContentProjectServiceTests
{
    private static (ContentProjectService Service, FakeContentProjectRepository Repo, FakePublishJobRepository Jobs, ContentProject Project) Build()
    {
        var project = ContentProject.Create("My Video", "cats", "storytelling", 60, "9:16", "en");
        var repo = new FakeContentProjectRepository(project);
        var jobs = new FakePublishJobRepository();
        var service = new ContentProjectService(repo, new FakeAssetService(), jobs);
        return (service, repo, jobs, project);
    }

    private sealed class EmptyContentProjectRepository : IContentProjectRepository
    {
        public bool DeleteWithProjectDataCalled { get; private set; }
        public Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ContentProject?>(null);
        public Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ContentProject>>(Array.Empty<ContentProject>());
        public Task AddAsync(ContentProject project, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteWithProjectDataAsync(Guid contentProjectId, CancellationToken cancellationToken = default) { DeleteWithProjectDataCalled = true; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Deleting_an_unknown_project_returns_false_without_touching_the_repository()
    {
        var repo = new EmptyContentProjectRepository();
        var service = new ContentProjectService(repo, new FakeAssetService(), new FakePublishJobRepository());

        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.False(result);
        Assert.False(repo.DeleteWithProjectDataCalled);
    }

    [Fact]
    public async Task A_busy_project_cannot_be_deleted()
    {
        var (service, repo, _, project) = Build();
        project.ReportProgress("generate", 0, 1, "Đang tạo nội dung...");

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.DeleteAsync(project.Id));

        Assert.Contains("xử lý", ex.Message);
        Assert.False(repo.DeleteWithProjectDataCalled);
    }

    [Fact]
    public async Task A_project_with_an_in_flight_publish_job_cannot_be_deleted()
    {
        var (service, repo, jobs, project) = Build();
        var job = PublishJob.Create(project.Id, PublishTarget.TikTok, Guid.NewGuid(), "path.mp4", "T", null, null, scheduledAtUtc: null);
        jobs.Jobs.Add(job); // Pending

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.DeleteAsync(project.Id));

        Assert.Contains("TikTok", ex.Message);
        Assert.False(repo.DeleteWithProjectDataCalled);
    }

    [Fact]
    public async Task A_project_with_a_still_retryable_failed_publish_job_cannot_be_deleted()
    {
        var (service, repo, jobs, project) = Build();
        var job = PublishJob.Create(project.Id, PublishTarget.YouTubeShorts, Guid.NewGuid(), "path.mp4", "T", null, null, scheduledAtUtc: null);
        job.MarkPublishing();
        job.MarkFailed("503 upstream", permanent: false); // CanRetry == true
        jobs.Jobs.Add(job);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.DeleteAsync(project.Id));

        Assert.False(repo.DeleteWithProjectDataCalled);
    }

    [Fact]
    public async Task A_project_whose_only_publish_jobs_are_terminal_can_be_deleted()
    {
        var (service, repo, jobs, project) = Build();

        var published = PublishJob.Create(project.Id, PublishTarget.YouTubeShorts, Guid.NewGuid(), "path.mp4", "T", null, null, scheduledAtUtc: null);
        published.MarkPublishing();
        published.MarkPublished("ext-1", "https://youtube.com/shorts/ext-1");
        jobs.Jobs.Add(published);

        var permanentlyFailed = PublishJob.Create(project.Id, PublishTarget.TikTok, Guid.NewGuid(), "path.mp4", "T", null, null, scheduledAtUtc: null);
        permanentlyFailed.MarkPublishing();
        permanentlyFailed.MarkFailed("400 bad request", permanent: true);
        jobs.Jobs.Add(permanentlyFailed);

        var result = await service.DeleteAsync(project.Id);

        Assert.True(result);
        Assert.True(repo.DeleteWithProjectDataCalled);
        // Publish history itself is untouched by ContentProjectService - the
        // repository's DeleteWithProjectDataAsync (not exercised by this fake)
        // is documented to never remove PublishJob rows.
        Assert.Equal(2, jobs.Jobs.Count);
    }

    [Fact]
    public async Task A_project_with_no_publish_jobs_at_all_can_be_deleted()
    {
        var (service, repo, _, project) = Build();

        var result = await service.DeleteAsync(project.Id);

        Assert.True(result);
        Assert.True(repo.DeleteWithProjectDataCalled);
    }
}
