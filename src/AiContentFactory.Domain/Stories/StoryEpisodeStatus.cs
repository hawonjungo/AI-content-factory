namespace AiContentFactory.Domain.Stories;

/// <summary>
/// Lifecycle of a single <see cref="StoryEpisode"/>. Kept minimal - unlike
/// <c>ContentProjectStatus</c> there is no branching state machine here, just
/// a straight-line progression.
/// </summary>
public enum StoryEpisodeStatus
{
    /// <summary>Created but no script/summary written yet.</summary>
    Draft = 0,

    /// <summary>Script and/or summary have been written.</summary>
    Scripted = 1,

    /// <summary>Finalized and folded into the Story's canonical <see cref="StoryState"/>.</summary>
    Completed = 2
}
