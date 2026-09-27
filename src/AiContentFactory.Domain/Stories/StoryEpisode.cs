using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Stories;

/// <summary>
/// One installment of a <see cref="Story"/>.
///
/// <see cref="Script"/> is a plain text field, NOT a foreign key into
/// <see cref="AiContentFactory.Domain.Scripts.Script"/> - the Story module is
/// intentionally self-contained and does not reference the ContentProject
/// pipeline.
/// </summary>
public class StoryEpisode : BaseEntity
{
    public Guid StoryId { get; private set; }
    public int EpisodeNumber { get; private set; }
    public string Title { get; private set; } = string.Empty;

    /// <summary>Plain-text script body. Not a FK to Domain.Scripts.Script.</summary>
    public string? Script { get; private set; }

    /// <summary>Short recap of what happened in this episode, used to build future continuity context.</summary>
    public string? Summary { get; private set; }

    /// <summary>Self-referencing FK into another episode of the same story; null when there is no predecessor.</summary>
    public Guid? PreviousEpisodeId { get; private set; }

    public StoryEpisodeStatus Status { get; private set; } = StoryEpisodeStatus.Draft;

    /// <summary>
    /// Point-in-time copy of the Story's <see cref="StoryState"/> fields,
    /// taken when this episode was completed via <see cref="Complete"/>. Null
    /// until then - a freshly-created episode has no snapshot yet.
    /// </summary>
    public StoryStateSnapshot? StoryStateSnapshot { get; private set; }

    /// <summary>
    /// The production plan written by the AI Pipeline's EpisodePlannerAgent
    /// before the script itself. Null until <see cref="SetOutline"/> is
    /// called.
    /// </summary>
    public StoryEpisodeOutline? Outline { get; private set; }

    /// <summary>
    /// Id of the <see cref="AiContentFactory.Domain.ContentProjects.ContentProject"/> that
    /// actually generates this episode's video. Deliberately NOT a relational FK - the
    /// Story module stays decoupled from the ContentProjects module (see
    /// <see cref="AiContentFactory.Infrastructure.Persistence.Configurations.StoryEpisodeConfiguration"/>).
    /// Null until <see cref="LinkContentProject"/> is called; an episode owns at most one
    /// ContentProject over its lifetime.
    /// </summary>
    public Guid? ContentProjectId { get; private set; }

    private StoryEpisode()
    {
        // EF Core
    }

    public static StoryEpisode Create(Guid storyId, int episodeNumber, string title, Guid? previousEpisodeId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (episodeNumber < 1)
        {
            throw new DomainException("EpisodeNumber must be at least 1.");
        }

        return new StoryEpisode
        {
            StoryId = storyId,
            EpisodeNumber = episodeNumber,
            Title = title.Trim(),
            PreviousEpisodeId = previousEpisodeId,
            Status = StoryEpisodeStatus.Draft
        };
    }

    /// <summary>Advances Draft -> Scripted the first time a script is recorded; a no-op status-wise once already Scripted/Completed.</summary>
    public void SetScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            throw new DomainException("Script is required.");
        }

        Script = script;

        if (Status == StoryEpisodeStatus.Draft)
        {
            Status = StoryEpisodeStatus.Scripted;
        }

        Touch();
    }

    /// <summary>Advances Draft -> Scripted the first time a summary is recorded; a no-op status-wise once already Scripted/Completed.</summary>
    public void SetSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new DomainException("Summary is required.");
        }

        Summary = summary;

        if (Status == StoryEpisodeStatus.Draft)
        {
            Status = StoryEpisodeStatus.Scripted;
        }

        Touch();
    }

    /// <summary>Replaces the current Episode Outline wholesale. Additive to the original Create surface - an episode may exist with no Outline until this is called.</summary>
    public void SetOutline(StoryEpisodeOutline outline)
    {
        Outline = outline ?? throw new DomainException("An episode outline is required.");
        Touch();
    }

    /// <summary>
    /// Records which ContentProject generates this episode's video. Idempotent when called
    /// again with the same id; throws if the episode is already linked to a DIFFERENT
    /// ContentProject, since an episode should own at most one over its lifetime.
    /// </summary>
    public void LinkContentProject(Guid contentProjectId)
    {
        if (ContentProjectId is { } existing)
        {
            if (existing == contentProjectId)
            {
                return;
            }

            throw new DomainException($"StoryEpisode '{Id}' is already linked to ContentProject '{existing}'.");
        }

        ContentProjectId = contentProjectId;
        Touch();
    }

    /// <summary>
    /// Clears a dangling link to a ContentProject that no longer exists (e.g. its video
    /// was deleted via <c>ContentProjectsController.Delete</c>, which does not cascade
    /// back into <see cref="StoryEpisode"/> - see <see cref="ContentProjectId"/>). A no-op
    /// status-wise; callers are expected to immediately create-and-link a fresh
    /// ContentProject afterward. Safe to call even if <see cref="ContentProjectId"/> is
    /// already null.
    /// </summary>
    public void ClearContentProjectLink()
    {
        ContentProjectId = null;
        Touch();
    }

    /// <summary>Finalizes the episode and freezes the Story's current state into <see cref="StoryStateSnapshot"/>.</summary>
    public void Complete(StoryStateSnapshot snapshot)
    {
        if (Status == StoryEpisodeStatus.Completed)
        {
            throw new DomainException($"StoryEpisode '{Id}' is already Completed.");
        }

        StoryStateSnapshot = snapshot ?? throw new DomainException("A state snapshot is required to complete an episode.");
        Status = StoryEpisodeStatus.Completed;
        Touch();
    }
}
