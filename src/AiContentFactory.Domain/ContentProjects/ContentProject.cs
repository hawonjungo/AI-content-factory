using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.ContentProjects;

/// <summary>
/// Aggregate root for a single piece of content moving through the
/// research -> script -> storyboard -> assets -> render -> QA -> publish pipeline.
///
/// Phase 1 only exercises Draft state via manual CRUD. The guarded transition
/// graph below already models the full pipeline so later phases (agents,
/// rendering, QA) only need to call TransitionTo — no schema changes required.
/// </summary>
public class ContentProject : BaseEntity
{
    private static readonly Dictionary<ContentProjectStatus, ContentProjectStatus[]> AllowedTransitions = new()
    {
        [ContentProjectStatus.Draft] = new[] { ContentProjectStatus.Researching, ContentProjectStatus.ScriptReady, ContentProjectStatus.Failed },
        [ContentProjectStatus.Researching] = new[] { ContentProjectStatus.ScriptReady, ContentProjectStatus.Failed },
        [ContentProjectStatus.ScriptReady] = new[] { ContentProjectStatus.StoryboardReady, ContentProjectStatus.Failed },
        [ContentProjectStatus.StoryboardReady] = new[] { ContentProjectStatus.Generating, ContentProjectStatus.Failed },
        [ContentProjectStatus.Generating] = new[] { ContentProjectStatus.Editing, ContentProjectStatus.Failed },
        [ContentProjectStatus.Editing] = new[] { ContentProjectStatus.QA, ContentProjectStatus.Failed },
        [ContentProjectStatus.QA] = new[] { ContentProjectStatus.AwaitingApproval, ContentProjectStatus.Editing, ContentProjectStatus.Failed },
        [ContentProjectStatus.AwaitingApproval] = new[] { ContentProjectStatus.Approved, ContentProjectStatus.Rejected },
        [ContentProjectStatus.Approved] = new[] { ContentProjectStatus.Published, ContentProjectStatus.Failed },
        [ContentProjectStatus.Rejected] = new[] { ContentProjectStatus.Editing, ContentProjectStatus.Draft },
        [ContentProjectStatus.Published] = Array.Empty<ContentProjectStatus>(),
        [ContentProjectStatus.Failed] = new[] { ContentProjectStatus.Draft }
    };

    public string Title { get; private set; } = string.Empty;
    public string? Topic { get; private set; }
    public string? Niche { get; private set; }
    public ContentProjectStatus Status { get; private set; } = ContentProjectStatus.Draft;
    public int TargetDurationSeconds { get; private set; }
    public string AspectRatio { get; private set; } = "9:16";
    public string Language { get; private set; } = "en";

    private ContentProject()
    {
        // EF Core
    }

    public static ContentProject Create(string title, string? topic, string? niche, int targetDurationSeconds, string aspectRatio, string language)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (targetDurationSeconds is < 1 or > 180)
        {
            throw new DomainException("TargetDurationSeconds must be between 1 and 180 seconds for short-form content.");
        }

        return new ContentProject
        {
            Title = title.Trim(),
            Topic = topic?.Trim(),
            Niche = niche?.Trim(),
            TargetDurationSeconds = targetDurationSeconds,
            AspectRatio = string.IsNullOrWhiteSpace(aspectRatio) ? "9:16" : aspectRatio.Trim(),
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim()
        };
    }

    public void UpdateDetails(string title, string? topic, string? niche, int targetDurationSeconds)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (targetDurationSeconds is < 1 or > 180)
        {
            throw new DomainException("TargetDurationSeconds must be between 1 and 180 seconds for short-form content.");
        }

        Title = title.Trim();
        Topic = topic?.Trim();
        Niche = niche?.Trim();
        TargetDurationSeconds = targetDurationSeconds;
        Touch();
    }

    public void TransitionTo(ContentProjectStatus newStatus)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(newStatus))
        {
            throw new DomainException($"Cannot transition ContentProject from '{Status}' to '{newStatus}'.");
        }

        Status = newStatus;
        Touch();
    }
}
