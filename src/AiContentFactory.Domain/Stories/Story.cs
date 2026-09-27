using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Domain.Stories;

/// <summary>
/// Aggregate root for a multi-episode story: the premise, its recurring
/// cast/locations "bible", the running canonical <see cref="StoryState"/>,
/// and the ordered set of episodes generated from it.
///
/// This is a self-contained module - it deliberately does NOT reference
/// <see cref="AiContentFactory.Domain.ContentProjects.ContentProject"/> or
/// <see cref="AiContentFactory.Domain.Scripts.Script"/>. Linking a Story's
/// episodes into the ContentProject pipeline is a separate, later concern.
///
/// Pure data model only: no cross-episode continuity logic, prompt building,
/// or AI generation calls live here.
/// </summary>
public class Story : BaseEntity
{
    /// <summary>Matches the column length of ContentProject.StylePresetId.</summary>
    public const int MaxStylePresetIdLength = 60;

    public string Title { get; private set; } = string.Empty;

    /// <summary>Overall story synopsis/logline.</summary>
    public string? Premise { get; private set; }

    public string? Niche { get; private set; }

    /// <summary>Default language new episodes inherit; mirrors ContentProject.Language.</summary>
    public string Language { get; private set; } = "en";

    /// <summary>Default aspect ratio new episodes inherit; mirrors ContentProject.AspectRatio.</summary>
    public string AspectRatio { get; private set; } = "9:16";

    /// <summary>
    /// Visual style preset chosen for the whole series (episodes inherit it);
    /// mirrors ContentProject.StylePresetId. Null = no series-level style chosen.
    /// </summary>
    public string? StylePresetId { get; private set; }

    /// <summary>
    /// The current canonical continuity state, as opposed to the per-episode
    /// point-in-time <see cref="StoryStateSnapshot"/>. Null until first created.
    /// </summary>
    public StoryState? State { get; private set; }

    /// <summary>
    /// The reusable "story bible" written once by the AI Pipeline's
    /// StoryPlannerAgent. Null until <see cref="SetBible"/> is called.
    /// </summary>
    public StoryBible? Bible { get; private set; }

    private readonly List<StoryCharacter> _characters = new();
    public IReadOnlyCollection<StoryCharacter> Characters => _characters.AsReadOnly();

    private readonly List<StoryLocation> _locations = new();
    public IReadOnlyCollection<StoryLocation> Locations => _locations.AsReadOnly();

    private readonly List<StoryEpisode> _episodes = new();
    public IReadOnlyCollection<StoryEpisode> Episodes => _episodes.AsReadOnly();

    private Story()
    {
        // EF Core
    }

    public static Story Create(string title, string? premise, string? niche, string? language, string? aspectRatio)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        return new Story
        {
            Title = title.Trim(),
            Premise = Normalize(premise),
            Niche = Normalize(niche),
            Language = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim(),
            AspectRatio = string.IsNullOrWhiteSpace(aspectRatio) ? "9:16" : aspectRatio.Trim()
        };
    }

    public void UpdateDetails(string title, string? premise, string? niche)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        Title = title.Trim();
        Premise = Normalize(premise);
        Niche = Normalize(niche);
        Touch();
    }

    /// <summary>
    /// Sets (or, with null/blank, clears) the series-level visual style preset.
    /// Additive to the original Create/UpdateDetails surface. Does not validate
    /// the id against the preset catalog - that is an application-layer concern.
    /// </summary>
    public void SetStylePreset(string? stylePresetId)
    {
        var normalized = Normalize(stylePresetId);
        if (normalized is { Length: > MaxStylePresetIdLength })
        {
            throw new DomainException($"Style preset id must be at most {MaxStylePresetIdLength} characters.");
        }

        StylePresetId = normalized;
        Touch();
    }

    /// <summary>Replaces the current Story Bible wholesale. Additive to the original Create/UpdateDetails surface - a Story may exist with no Bible until this is called.</summary>
    public void SetBible(StoryBible bible)
    {
        Bible = bible ?? throw new DomainException("A story bible is required.");
        Touch();
    }

    /// <summary>
    /// Attaches an already-persisted <see cref="StoryCharacter"/> to this
    /// aggregate's in-memory <see cref="Characters"/> collection. In
    /// production this population normally happens transparently via EF
    /// Core's Include-based materialization of the private backing field
    /// (characters/locations are created and saved independently via
    /// IStoryRepository.AddCharacterAsync, not through this aggregate), so
    /// this method exists to give test doubles (which don't go through EF)
    /// a public way to reproduce that same state. Not intended for use
    /// outside of repository/test population.
    /// </summary>
    public void AttachCharacter(StoryCharacter character) => _characters.Add(character);

    /// <summary>Same purpose as <see cref="AttachCharacter"/>, for locations.</summary>
    public void AttachLocation(StoryLocation location) => _locations.Add(location);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
