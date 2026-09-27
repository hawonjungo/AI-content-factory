using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryEpisodeTests
{
    [Fact]
    public void LinkContentProject_sets_the_value()
    {
        var episode = StoryEpisode.Create(Guid.NewGuid(), 1, "Ep 1", null);
        var contentProjectId = Guid.NewGuid();

        episode.LinkContentProject(contentProjectId);

        Assert.Equal(contentProjectId, episode.ContentProjectId);
    }

    [Fact]
    public void LinkContentProject_with_same_id_is_a_no_op()
    {
        var episode = StoryEpisode.Create(Guid.NewGuid(), 1, "Ep 1", null);
        var contentProjectId = Guid.NewGuid();

        episode.LinkContentProject(contentProjectId);
        episode.LinkContentProject(contentProjectId);

        Assert.Equal(contentProjectId, episode.ContentProjectId);
    }

    [Fact]
    public void LinkContentProject_with_a_different_id_throws()
    {
        var episode = StoryEpisode.Create(Guid.NewGuid(), 1, "Ep 1", null);
        episode.LinkContentProject(Guid.NewGuid());

        Assert.Throws<DomainException>(() => episode.LinkContentProject(Guid.NewGuid()));
    }

    [Fact]
    public void ClearContentProjectLink_resets_the_value_to_null()
    {
        var episode = StoryEpisode.Create(Guid.NewGuid(), 1, "Ep 1", null);
        episode.LinkContentProject(Guid.NewGuid());

        episode.ClearContentProjectLink();

        Assert.Null(episode.ContentProjectId);
    }

    [Fact]
    public void ClearContentProjectLink_is_safe_to_call_when_already_null()
    {
        var episode = StoryEpisode.Create(Guid.NewGuid(), 1, "Ep 1", null);

        episode.ClearContentProjectLink();

        Assert.Null(episode.ContentProjectId);
    }

    [Fact]
    public void LinkContentProject_after_ClearContentProjectLink_relinks_to_a_new_id()
    {
        var episode = StoryEpisode.Create(Guid.NewGuid(), 1, "Ep 1", null);
        episode.LinkContentProject(Guid.NewGuid());
        episode.ClearContentProjectLink();

        var newProjectId = Guid.NewGuid();
        episode.LinkContentProject(newProjectId);

        Assert.Equal(newProjectId, episode.ContentProjectId);
    }
}
