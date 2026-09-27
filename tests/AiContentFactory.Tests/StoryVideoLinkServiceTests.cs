using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Scripts;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Scripts;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class StoryVideoLinkServiceTests
{
    private const string WellFormedScript =
        "HOOK:\nDid you know...\n\nINTRODUCTION:\nOnce upon a time.\n\nBODY:\nThings happened.\n\nESCALATION:\nThings got worse.\n\nPAYOFF:\nIt all made sense.\n\nCALL TO ACTION:\nFollow for more.";

    private sealed class FakeMultiContentProjectRepository : IContentProjectRepository
    {
        public Dictionary<Guid, ContentProject> Projects { get; } = new();

        public Task<ContentProject?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Projects.GetValueOrDefault(id));

        public Task<IReadOnlyList<ContentProject>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContentProject>>(Projects.Values.ToList());

        public Task AddAsync(ContentProject project, CancellationToken cancellationToken = default)
        {
            Projects[project.Id] = project;
            return Task.CompletedTask;
        }

        public Task DeleteWithProjectDataAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
        {
            Projects.Remove(contentProjectId);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeScriptRepository : IScriptRepository
    {
        public Dictionary<Guid, Script> ScriptsByProject { get; } = new();

        public Task<Script?> GetByContentProjectIdAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScriptsByProject.GetValueOrDefault(contentProjectId));

        public Task AddAsync(Script script, CancellationToken cancellationToken = default)
        {
            ScriptsByProject[script.ContentProjectId] = script;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record Fixture(
        StoryVideoLinkService Service,
        FakeStoryRepository StoryRepo,
        FakeMultiContentProjectRepository ProjectRepo,
        FakeScriptRepository ScriptRepo,
        FakeAssetReferenceRepository ReferenceRepo,
        ContentProjectService ContentProjectService,
        IPresetService PresetService);

    private static Fixture Build()
    {
        var storyRepo = new FakeStoryRepository();
        var projectRepo = new FakeMultiContentProjectRepository();
        var scriptRepo = new FakeScriptRepository();
        var referenceRepo = new FakeAssetReferenceRepository();

        var contentProjectService = new ContentProjectService(projectRepo, new FakeAssetService(), new FakePublishJobRepository());
        var scriptService = new ScriptService(scriptRepo);
        var presetService = new PresetService(projectRepo, Options.Create(new PricingOptions()));

        var service = new StoryVideoLinkService(storyRepo, contentProjectService, scriptService, referenceRepo, presetService);

        return new Fixture(service, storyRepo, projectRepo, scriptRepo, referenceRepo, contentProjectService, presetService);
    }

    private static (Story Story, StoryEpisode Episode) SeedStoryWithScriptedEpisode(FakeStoryRepository repo, string? script = WellFormedScript, Guid? previousEpisodeId = null)
    {
        var story = Story.Create("My Series", "A premise", "drama", "en", "9:16");
        repo.Stories[story.Id] = story;

        var episode = StoryEpisode.Create(story.Id, 1, "Pilot", previousEpisodeId);
        if (script is not null)
        {
            episode.SetScript(script);
        }

        repo.Episodes[episode.Id] = episode;
        return (story, episode);
    }

    [Fact]
    public async Task Unknown_story_returns_null()
    {
        var f = Build();

        var result = await f.Service.CreateOrOpenVideoAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task Unknown_episode_returns_null()
    {
        var f = Build();
        var story = Story.Create("My Series", null, null, null, null);
        f.StoryRepo.Stories[story.Id] = story;

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task An_episode_with_no_script_and_no_existing_link_throws()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo, script: null);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id));
    }

    [Fact]
    public async Task Create_video_happy_path_creates_a_ScriptReady_project_with_parsed_sections_and_links_the_episode()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        Assert.True(result!.Created);
        Assert.True(f.ProjectRepo.Projects.ContainsKey(result.ContentProjectId));

        var project = f.ProjectRepo.Projects[result.ContentProjectId];
        Assert.Equal(nameof(ContentProjectStatus.ScriptReady), project.Status.ToString());
        Assert.Equal("9:16", project.AspectRatio);
        Assert.Equal("en", project.Language);
        Assert.Contains(story.Title, project.Title);
        Assert.Contains(episode.Title, project.Title);

        var script = f.ScriptRepo.ScriptsByProject[result.ContentProjectId];
        Assert.Equal("Did you know...", script.Hook);
        Assert.Equal("Once upon a time.", script.Introduction);
        Assert.Equal("Things happened.", script.Body);
        Assert.Equal("Things got worse.", script.Escalation);
        Assert.Equal("It all made sense.", script.Payoff);
        Assert.Equal("Follow for more.", script.CallToAction);

        Assert.Equal(result.ContentProjectId, episode.ContentProjectId);
    }

    [Fact]
    public async Task Open_video_path_returns_the_existing_project_and_does_not_create_a_second_one()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var first = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(first);
        Assert.True(first!.Created);
        Assert.Single(f.ProjectRepo.Projects);

        var second = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(second);
        Assert.False(second!.Created);
        Assert.Equal(first.ContentProjectId, second.ContentProjectId);
        Assert.Single(f.ProjectRepo.Projects); // no second project was created
    }

    [Fact]
    public async Task Malformed_script_falls_back_to_putting_everything_in_Body()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo, script: "Just some free-form text the user typed in manually.");

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        var script = f.ScriptRepo.ScriptsByProject[result!.ContentProjectId];
        Assert.Equal("Just some free-form text the user typed in manually.", script.Body);
        Assert.Equal(string.Empty, script.Hook);
        Assert.Equal(string.Empty, script.Introduction);
        Assert.Equal(string.Empty, script.Escalation);
        Assert.Equal(string.Empty, script.Payoff);
        Assert.Equal(string.Empty, script.CallToAction);
    }

    [Fact]
    public async Task Visual_references_are_carried_forward_from_an_approved_previous_episode_project()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        Assert.NotNull(previousResult);

        var approvedRef = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Character, "refs/char.png", "a hero", "gemini");
        approvedRef.Approve();
        f.ReferenceRepo.Rows.Add(approvedRef);

        // A Generated-but-not-approved variant on the same project must NOT be carried forward.
        var unapprovedRef = AssetReference.CreateGenerated(previousResult.ContentProjectId, AssetReferenceType.Environment, "refs/env.png", "a forest", "gemini");
        f.ReferenceRepo.Rows.Add(unapprovedRef);

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.NotNull(nextResult);
        var copiedRefs = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == nextResult!.ContentProjectId).ToList();
        Assert.Single(copiedRefs);
        Assert.Equal(AssetReferenceType.Character, copiedRefs[0].Type);
        Assert.Equal(AssetReferenceStatus.Approved, copiedRefs[0].Status);
        Assert.Equal("refs/char.png", copiedRefs[0].ImagePath);

        // The previous project's own rows are untouched (still 2: approved + unapproved).
        Assert.Equal(2, f.ReferenceRepo.Rows.Count(r => r.ContentProjectId == previousResult.ContentProjectId));
    }

    /// <summary>
    /// Regression coverage: without carrying the predecessor's style preset
    /// forward, a new episode's ContentProject had no StylePresetId at all
    /// and silently fell back to VideoPromptBuilder.DefaultStyleGuidance
    /// ("Photorealistic documentary...") on every single new episode - see
    /// CarryForwardStylePresetAsync's own remarks for the real incident.
    /// </summary>
    [Fact]
    public async Task Style_preset_is_carried_forward_from_the_previous_episode_project()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        Assert.NotNull(previousResult);
        await f.PresetService.ApplyAsync(previousResult!.ContentProjectId, new ApplyPresetsRequest(null, "cat-travel-stylized-realism", null, null));

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.NotNull(nextResult);
        var nextProject = f.ProjectRepo.Projects[nextResult!.ContentProjectId];
        Assert.Equal("cat-travel-stylized-realism", nextProject.StylePresetId);
    }

    [Fact]
    public async Task No_style_preset_is_carried_forward_when_the_predecessor_never_had_one_set()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        Assert.NotNull(previousResult);

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.NotNull(nextResult);
        var nextProject = f.ProjectRepo.Projects[nextResult!.ContentProjectId];
        Assert.Null(nextProject.StylePresetId);
    }

    // ---- Style precedence: Story.StylePresetId > predecessor project style > none ----

    private static StoryEpisode SeedNextEpisode(Fixture f, Story story, StoryEpisode previousEpisode)
    {
        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;
        return nextEpisode;
    }

    [Fact]
    public async Task Story_style_is_applied_to_the_first_episode_which_has_no_predecessor()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        story.SetStylePreset("pixar-3d");

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.Equal("pixar-3d", f.ProjectRepo.Projects[result!.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task Story_style_is_applied_using_the_canonical_catalog_id()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        story.SetStylePreset("PIXAR-3D"); // the domain does not canonicalise; the catalog lookup is case-insensitive

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.Equal("pixar-3d", f.ProjectRepo.Projects[result!.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task Story_style_wins_over_the_predecessor_projects_style()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        await f.PresetService.ApplyAsync(previousResult!.ContentProjectId, new ApplyPresetsRequest(null, "cat-travel-stylized-realism", null, null));
        story.SetStylePreset("pixar-3d");
        var nextEpisode = SeedNextEpisode(f, story, previousEpisode);

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.Equal("pixar-3d", f.ProjectRepo.Projects[nextResult!.ContentProjectId].StylePresetId);
        // Changing the Story style never retroactively rewrites an already-created episode project.
        Assert.Equal("cat-travel-stylized-realism", f.ProjectRepo.Projects[previousResult.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task Predecessor_style_is_used_when_the_story_has_no_style()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        await f.PresetService.ApplyAsync(previousResult!.ContentProjectId, new ApplyPresetsRequest(null, "anime", null, null));
        Assert.Null(story.StylePresetId);
        var nextEpisode = SeedNextEpisode(f, story, previousEpisode);

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.Equal("anime", f.ProjectRepo.Projects[nextResult!.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task No_style_is_applied_when_neither_the_story_nor_a_predecessor_has_one()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.Null(f.ProjectRepo.Projects[result!.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task A_story_style_that_is_no_longer_in_the_catalog_is_skipped_and_the_predecessor_style_is_used()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        await f.PresetService.ApplyAsync(previousResult!.ContentProjectId, new ApplyPresetsRequest(null, "anime", null, null));
        story.SetStylePreset("a-style-that-was-removed");
        var nextEpisode = SeedNextEpisode(f, story, previousEpisode);

        // Must not throw "Unknown style preset" and block episode creation.
        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.True(nextResult!.Created);
        Assert.Equal("anime", f.ProjectRepo.Projects[nextResult.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task Opening_an_already_linked_episode_never_reapplies_a_changed_story_style()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        story.SetStylePreset("anime");
        var first = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        story.SetStylePreset("pixar-3d");
        var second = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.False(second!.Created);
        Assert.Equal("anime", f.ProjectRepo.Projects[first!.ContentProjectId].StylePresetId);
    }

    [Fact]
    public async Task Multiple_labeled_Approved_references_all_copy_forward_from_the_predecessor_project()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        Assert.NotNull(previousResult);

        var miloRef = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Character, "refs/milo.png", "milo prompt", "gemini", label: "Milo");
        miloRef.Approve();
        f.ReferenceRepo.Rows.Add(miloRef);

        var mimiRef = AssetReference.CreateGenerated(previousResult.ContentProjectId, AssetReferenceType.Character, "refs/mimi.png", "mimi prompt", "gemini", label: "Mimi");
        mimiRef.Approve();
        f.ReferenceRepo.Rows.Add(mimiRef);

        var locationRef = AssetReference.CreateGenerated(previousResult.ContentProjectId, AssetReferenceType.Environment, "refs/halong.png", "halong prompt", "gemini", label: "Ha Long Bay");
        locationRef.Approve();
        f.ReferenceRepo.Rows.Add(locationRef);

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.NotNull(nextResult);
        var copiedRefs = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == nextResult!.ContentProjectId).ToList();
        Assert.Equal(3, copiedRefs.Count);
        Assert.Contains(copiedRefs, r => r.Type == AssetReferenceType.Character && r.Label == "Milo" && r.ImagePath == "refs/milo.png");
        Assert.Contains(copiedRefs, r => r.Type == AssetReferenceType.Character && r.Label == "Mimi" && r.ImagePath == "refs/mimi.png");
        Assert.Contains(copiedRefs, r => r.Type == AssetReferenceType.Environment && r.Label == "Ha Long Bay" && r.ImagePath == "refs/halong.png");
        Assert.All(copiedRefs, r => Assert.Equal(AssetReferenceStatus.Approved, r.Status));
    }

    [Fact]
    public async Task No_previous_episode_project_or_no_approved_references_is_not_an_error()
    {
        var f = Build();
        // Episode 1 has no PreviousEpisodeId at all.
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        Assert.Empty(f.ReferenceRepo.Rows);
    }

    [Fact]
    public async Task Story_level_approved_character_reference_seeds_episode_1s_project_when_there_is_no_predecessor()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var character = StoryCharacter.Create(story.Id, "Nova", "A witty fox detective.", "Orange fur, blue coat.");
        character.MarkReferenceImageGenerated("stories/story/characters/nova/ref.png", "a fox", "gemini");
        character.ApproveReferenceImage();
        story.AttachCharacter(character);
        f.StoryRepo.Characters[character.Id] = character;

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        var seeded = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == result!.ContentProjectId).ToList();
        var characterRef = Assert.Single(seeded, r => r.Type == AssetReferenceType.Character);
        Assert.Equal(AssetReferenceStatus.Approved, characterRef.Status);
        Assert.Equal("stories/story/characters/nova/ref.png", characterRef.ImagePath);
    }

    [Fact]
    public async Task All_approved_Story_characters_are_seeded_as_their_own_labeled_reference_rows()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var milo = StoryCharacter.Create(story.Id, "Milo", "A curious cat.", "Grey tabby.");
        milo.MarkReferenceImageGenerated("stories/story/characters/milo/ref.png", "a cat", "gemini");
        milo.ApproveReferenceImage();
        story.AttachCharacter(milo);
        f.StoryRepo.Characters[milo.Id] = milo;

        var mimi = StoryCharacter.Create(story.Id, "Mimi", "A playful kitten.", "White fur.");
        mimi.MarkReferenceImageGenerated("stories/story/characters/mimi/ref.png", "a kitten", "gemini");
        mimi.ApproveReferenceImage();
        story.AttachCharacter(mimi);
        f.StoryRepo.Characters[mimi.Id] = mimi;

        var result = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        var seeded = f.ReferenceRepo.Rows
            .Where(r => r.ContentProjectId == result!.ContentProjectId && r.Type == AssetReferenceType.Character)
            .ToList();

        Assert.Equal(2, seeded.Count);
        var miloRef = Assert.Single(seeded, r => r.Label == "Milo");
        var mimiRef = Assert.Single(seeded, r => r.Label == "Mimi");
        Assert.Equal(AssetReferenceStatus.Approved, miloRef.Status);
        Assert.Equal("stories/story/characters/milo/ref.png", miloRef.ImagePath);
        Assert.Equal(AssetReferenceStatus.Approved, mimiRef.Status);
        Assert.Equal("stories/story/characters/mimi/ref.png", mimiRef.ImagePath);
    }

    [Fact]
    public async Task Story_level_approved_character_image_wins_over_the_predecessor_copy_for_the_same_named_character()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        // No Story-level character yet at episode-1 creation time, so nothing
        // is auto-seeded onto the predecessor's own project here.
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        Assert.NotNull(previousResult);

        // The predecessor episode's project has its own approved reference for
        // "Nova" - same (Type, Label) as the Story-level image below.
        var predecessorRef = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Character, "refs/predecessor-char.png", "predecessor prompt", "gemini", label: "Nova");
        predecessorRef.Approve();
        f.ReferenceRepo.Rows.Add(predecessorRef);

        // A Story-level approved character reference exists too, by the time episode 2 is created.
        var character = StoryCharacter.Create(story.Id, "Nova", "A witty fox detective.", "Orange fur, blue coat.");
        character.MarkReferenceImageGenerated("stories/story/characters/nova/ref.png", "a fox", "gemini");
        character.ApproveReferenceImage();
        story.AttachCharacter(character);
        f.StoryRepo.Characters[character.Id] = character;

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        Assert.NotNull(nextResult);
        var seeded = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == nextResult!.ContentProjectId).ToList();
        var characterRef = Assert.Single(seeded, r => r.Type == AssetReferenceType.Character);
        // The canonical Story-level image wins; the predecessor's copy is not carried over.
        Assert.Equal("stories/story/characters/nova/ref.png", characterRef.ImagePath);
        Assert.Equal("Nova", characterRef.Label);
    }

    [Fact]
    public async Task Predecessor_copy_is_used_only_for_characters_that_have_no_Story_level_approved_image()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        Assert.NotNull(previousResult);

        foreach (var (label, path) in new[] { ("Nova", "refs/predecessor-nova.png"), ("Pip", "refs/predecessor-pip.png"), ("Quill", "refs/predecessor-quill.png") })
        {
            var row = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Character, path, "p", "gemini", label: label);
            row.Approve();
            f.ReferenceRepo.Rows.Add(row);
        }

        // Nova: Story-level image approved -> canonical wins.
        AttachApprovedCharacter(f.StoryRepo, story, "stories/story/characters/nova/ref.png");
        // Pip: exists in the Story but its image was only generated, never approved -> predecessor copy stays.
        var pip = StoryCharacter.Create(story.Id, "Pip", "A tiny bird.", "Blue feathers.");
        pip.MarkReferenceImageGenerated("stories/story/characters/pip/unapproved.png", "a bird", "gemini");
        story.AttachCharacter(pip);
        f.StoryRepo.Characters[pip.Id] = pip;
        // Quill: not a Story character at all -> predecessor copy stays.

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        var rows = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == nextResult!.ContentProjectId).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("stories/story/characters/nova/ref.png", Assert.Single(rows, r => r.Label == "Nova").ImagePath);
        Assert.Equal("refs/predecessor-pip.png", Assert.Single(rows, r => r.Label == "Pip").ImagePath);
        Assert.Equal("refs/predecessor-quill.png", Assert.Single(rows, r => r.Label == "Quill").ImagePath);
        Assert.All(rows, r => Assert.Equal(AssetReferenceStatus.Approved, r.Status));
    }

    [Fact]
    public async Task A_replaced_Story_level_character_image_reaches_a_new_episode_and_leaves_existing_episode_projects_untouched()
    {
        var f = Build();
        var (story, episode1) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var nova = AttachApprovedCharacter(f.StoryRepo, story, "stories/story/characters/nova/v1.png");

        var project1 = await f.Service.CreateOrOpenVideoAsync(story.Id, episode1.Id);
        Assert.Equal("stories/story/characters/nova/v1.png", Assert.Single(f.ReferenceRepo.Rows, r => r.ContentProjectId == project1!.ContentProjectId).ImagePath);

        // The user later generates a better image, then confirms replacing the approved one.
        nova.SetReferenceCandidate("stories/story/characters/nova/v2.png", "p2", "gemini", ReferenceImageSource.Generated);
        nova.PromotePendingReferenceImage();

        var episode2 = StoryEpisode.Create(story.Id, 2, "Episode 2", episode1.Id);
        episode2.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[episode2.Id] = episode2;
        var project2 = await f.Service.CreateOrOpenVideoAsync(story.Id, episode2.Id);

        Assert.Equal("stories/story/characters/nova/v2.png", Assert.Single(f.ReferenceRepo.Rows, r => r.ContentProjectId == project2!.ContentProjectId).ImagePath);
        // Episode 1's project still holds the image it was created with.
        Assert.Equal("stories/story/characters/nova/v1.png", Assert.Single(f.ReferenceRepo.Rows, r => r.ContentProjectId == project1!.ContentProjectId).ImagePath);
    }

    [Theory]
    [InlineData("nova")]
    [InlineData("NOVA")]
    [InlineData("  Nova  ")]
    public async Task Character_names_match_project_labels_case_insensitively_so_the_Story_image_still_wins_over_the_predecessor_copy(string predecessorLabel)
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        var predecessorRef = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Character, "refs/predecessor-nova.png", "p", "gemini", label: predecessorLabel);
        predecessorRef.Approve();
        f.ReferenceRepo.Rows.Add(predecessorRef);
        AttachApprovedCharacter(f.StoryRepo, story, "stories/story/characters/nova/ref.png");

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        var rows = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == nextResult!.ContentProjectId && r.Type == AssetReferenceType.Character).ToList();
        var row = Assert.Single(rows); // no duplicate under a differently-cased label
        Assert.Equal("stories/story/characters/nova/ref.png", row.ImagePath);
        Assert.Equal("Nova", row.Label);
    }

    [Fact]
    public async Task A_differently_cased_predecessor_row_is_kept_when_the_Story_character_has_no_approved_image()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);
        var predecessorRef = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Character, "refs/predecessor-nova.png", "p", "gemini", label: "NOVA");
        predecessorRef.Approve();
        f.ReferenceRepo.Rows.Add(predecessorRef);
        var unapproved = StoryCharacter.Create(story.Id, "Nova", "d", "v");
        unapproved.MarkReferenceImageGenerated("stories/story/characters/nova/unapproved.png", "p", "gemini");
        story.AttachCharacter(unapproved);
        f.StoryRepo.Characters[unapproved.Id] = unapproved;

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        var row = Assert.Single(f.ReferenceRepo.Rows, r => r.ContentProjectId == nextResult!.ContentProjectId && r.Type == AssetReferenceType.Character);
        Assert.Equal("refs/predecessor-nova.png", row.ImagePath);
    }

    [Fact]
    public async Task Sync_matches_labels_case_insensitively_it_neither_duplicates_a_differently_cased_row_nor_calls_a_matching_image_outdated()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        var sameImage = AssetReference.CreateGenerated(created!.ContentProjectId, AssetReferenceType.Character, "stories/story/characters/nova/ref.png", "p", "gemini", label: "nova");
        sameImage.Approve();
        f.ReferenceRepo.Rows.Add(sameImage);
        AttachApprovedCharacter(f.StoryRepo, story, "stories/story/characters/nova/ref.png");

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.Empty(result!.SyncedLabels);
        Assert.Empty(result.OutdatedLabels);
        Assert.Single(f.ReferenceRepo.Rows, r => r.ContentProjectId == created.ContentProjectId && r.Type == AssetReferenceType.Character);
    }

    [Fact]
    public async Task Sync_reports_a_differently_cased_row_with_an_older_image_as_outdated()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        var older = AssetReference.CreateGenerated(created!.ContentProjectId, AssetReferenceType.Character, "refs/older.png", "p", "gemini", label: " NOVA ");
        older.Approve();
        f.ReferenceRepo.Rows.Add(older);
        AttachApprovedCharacter(f.StoryRepo, story, "stories/story/characters/nova/ref.png");

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.Equal(new[] { "Nova" }, result!.OutdatedLabels);
        Assert.Empty(result.SyncedLabels);
    }

    [Fact]
    public async Task Environment_behaviour_is_unchanged_the_predecessor_copy_still_wins_over_the_Story_location_image()
    {
        var f = Build();
        var (story, previousEpisode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var previousResult = await f.Service.CreateOrOpenVideoAsync(story.Id, previousEpisode.Id);

        var predecessorEnv = AssetReference.CreateGenerated(previousResult!.ContentProjectId, AssetReferenceType.Environment, "refs/predecessor-env.png", "p", "gemini", label: "Neon Alley");
        predecessorEnv.Approve();
        f.ReferenceRepo.Rows.Add(predecessorEnv);

        var location = StoryLocation.Create(story.Id, "Neon Alley", "A back alley.", "Rain-slicked pavement.");
        location.MarkReferenceImageGenerated("stories/story/locations/alley/ref.png", "an alley", "gemini");
        location.ApproveReferenceImage();
        story.AttachLocation(location);
        f.StoryRepo.Locations[location.Id] = location;

        var nextEpisode = StoryEpisode.Create(story.Id, 2, "Episode 2", previousEpisode.Id);
        nextEpisode.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[nextEpisode.Id] = nextEpisode;

        var nextResult = await f.Service.CreateOrOpenVideoAsync(story.Id, nextEpisode.Id);

        var envRef = Assert.Single(f.ReferenceRepo.Rows, r => r.ContentProjectId == nextResult!.ContentProjectId && r.Type == AssetReferenceType.Environment);
        Assert.Equal("refs/predecessor-env.png", envRef.ImagePath);
    }

    [Fact]
    public async Task Multi_episode_isolation_each_episode_gets_its_own_project_and_creating_the_second_does_not_touch_the_first()
    {
        var f = Build();
        var story = Story.Create("My Series", null, null, null, null);
        f.StoryRepo.Stories[story.Id] = story;

        var episode1 = StoryEpisode.Create(story.Id, 1, "Ep 1", null);
        episode1.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[episode1.Id] = episode1;

        var episode2 = StoryEpisode.Create(story.Id, 2, "Ep 2", episode1.Id);
        episode2.SetScript(WellFormedScript);
        f.StoryRepo.Episodes[episode2.Id] = episode2;

        var result1 = await f.Service.CreateOrOpenVideoAsync(story.Id, episode1.Id);
        var project1StatusBefore = f.ProjectRepo.Projects[result1!.ContentProjectId].Status;
        var project1UpdatedAtBefore = f.ProjectRepo.Projects[result1.ContentProjectId].UpdatedAt;

        var result2 = await f.Service.CreateOrOpenVideoAsync(story.Id, episode2.Id);

        Assert.NotEqual(result1.ContentProjectId, result2!.ContentProjectId);
        Assert.Equal(2, f.ProjectRepo.Projects.Count);

        // Episode 1's project is untouched by Episode 2's creation.
        Assert.Equal(project1StatusBefore, f.ProjectRepo.Projects[result1.ContentProjectId].Status);
        Assert.Equal(project1UpdatedAtBefore, f.ProjectRepo.Projects[result1.ContentProjectId].UpdatedAt);

        Assert.Equal(result1.ContentProjectId, episode1.ContentProjectId);
        Assert.Equal(result2.ContentProjectId, episode2.ContentProjectId);
    }

    [Fact]
    public async Task Create_video_self_heals_when_the_linked_ContentProject_no_longer_exists()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var first = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(first);
        var deadProjectId = first!.ContentProjectId;

        // Simulate ContentProjectsController.Delete: the project (and its
        // project data) is gone, but the episode's ContentProjectId is left
        // dangling - it is NOT a relational FK (see StoryEpisode.ContentProjectId).
        f.ProjectRepo.Projects.Remove(deadProjectId);

        var second = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        Assert.NotNull(second);
        Assert.True(second!.Created);
        Assert.NotEqual(deadProjectId, second.ContentProjectId);
        Assert.True(f.ProjectRepo.Projects.ContainsKey(second.ContentProjectId));
        Assert.Equal(second.ContentProjectId, episode.ContentProjectId);
    }

    // ---- SyncStoryReferencesAsync ----

    private static StoryCharacter AttachApprovedCharacter(FakeStoryRepository repo, Story story, string imagePath = "stories/story/characters/nova/ref.png")
    {
        var character = StoryCharacter.Create(story.Id, "Nova", "A witty fox detective.", "Orange fur, blue coat.");
        character.MarkReferenceImageGenerated(imagePath, "a fox", "gemini");
        character.ApproveReferenceImage();
        story.AttachCharacter(character);
        repo.Characters[character.Id] = character;
        return character;
    }

    [Fact]
    public async Task Sync_seeds_the_character_slot_when_the_projects_own_reference_was_never_approved()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        // Episode's project already exists (as if created before the Story-level image existed).
        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(created);
        Assert.Empty(f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == created!.ContentProjectId).ToList());

        // Story-level character image is approved after the project was created.
        AttachApprovedCharacter(f.StoryRepo, story);

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        Assert.Equal(created!.ContentProjectId, result!.ContentProjectId);
        Assert.Contains("Character: Nova", result.SyncedLabels);

        var seeded = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == created.ContentProjectId).ToList();
        var characterRef = Assert.Single(seeded, r => r.Type == AssetReferenceType.Character);
        Assert.Equal(AssetReferenceStatus.Approved, characterRef.Status);
        Assert.Equal("stories/story/characters/nova/ref.png", characterRef.ImagePath);
    }

    [Fact]
    public async Task Sync_does_not_overwrite_an_already_approved_reference()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(created);

        // The project already has its own approved "Nova" reference (different
        // image than the Story's, same name - the same (Type, Label) pair).
        var ownRef = AssetReference.CreateGenerated(created!.ContentProjectId, AssetReferenceType.Character, "refs/own-char.png", "own prompt", "gemini", label: "Nova");
        ownRef.Approve();
        f.ReferenceRepo.Rows.Add(ownRef);

        AttachApprovedCharacter(f.StoryRepo, story);

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        Assert.DoesNotContain("Character: Nova", result!.SyncedLabels);

        var characterRefs = f.ReferenceRepo.Rows.Where(r => r.ContentProjectId == created.ContentProjectId && r.Type == AssetReferenceType.Character).ToList();
        var onlyRef = Assert.Single(characterRefs);
        Assert.Equal("refs/own-char.png", onlyRef.ImagePath);

        // Detection only: the project's approved image differs from the Story's, so it is reported, never replaced.
        Assert.Equal(new[] { "Nova" }, result.OutdatedLabels);
    }

    [Fact]
    public async Task Sync_reports_a_character_as_outdated_when_the_Story_image_was_replaced_but_never_modifies_the_project()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        var nova = AttachApprovedCharacter(f.StoryRepo, story, "stories/story/characters/nova/v1.png");
        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);

        // Up to date right after creation.
        var before = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);
        Assert.Empty(before!.OutdatedLabels);
        Assert.Empty(before.SyncedLabels);

        nova.SetReferenceCandidate("stories/story/characters/nova/v2.png", "p2", "gemini", ReferenceImageSource.Generated);
        nova.PromotePendingReferenceImage();
        var rowsBefore = f.ReferenceRepo.Rows.Select(r => (r.Id, r.ImagePath, r.Status)).ToList();

        var after = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.Equal(new[] { "Nova" }, after!.OutdatedLabels);
        Assert.Empty(after.SyncedLabels);
        Assert.Equal(rowsBefore, f.ReferenceRepo.Rows.Select(r => (r.Id, r.ImagePath, r.Status)).ToList());
        Assert.Equal(created!.ContentProjectId, after.ContentProjectId);
    }

    [Fact]
    public async Task Sync_does_not_report_outdated_for_a_character_that_is_simply_missing_from_the_project_it_is_seeded_instead()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);
        await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        AttachApprovedCharacter(f.StoryRepo, story);

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.Empty(result!.OutdatedLabels);
        Assert.Contains("Character: Nova", result.SyncedLabels);
    }

    [Fact]
    public async Task Sync_adds_a_missing_named_character_without_touching_an_already_approved_one()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(created);

        // Project already has "Milo" approved.
        var miloRef = AssetReference.CreateGenerated(created!.ContentProjectId, AssetReferenceType.Character, "refs/milo.png", "milo prompt", "gemini", label: "Milo");
        miloRef.Approve();
        f.ReferenceRepo.Rows.Add(miloRef);

        // Story now has BOTH Milo and Mimi approved.
        var milo = StoryCharacter.Create(story.Id, "Milo", "A curious cat.", "Grey tabby.");
        milo.MarkReferenceImageGenerated("stories/story/characters/milo/ref.png", "a cat", "gemini");
        milo.ApproveReferenceImage();
        story.AttachCharacter(milo);
        f.StoryRepo.Characters[milo.Id] = milo;

        var mimi = StoryCharacter.Create(story.Id, "Mimi", "A playful kitten.", "White fur.");
        mimi.MarkReferenceImageGenerated("stories/story/characters/mimi/ref.png", "a kitten", "gemini");
        mimi.ApproveReferenceImage();
        story.AttachCharacter(mimi);
        f.StoryRepo.Characters[mimi.Id] = mimi;

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        Assert.Contains("Character: Mimi", result!.SyncedLabels);
        Assert.DoesNotContain("Character: Milo", result.SyncedLabels);

        var characterRefs = f.ReferenceRepo.Rows
            .Where(r => r.ContentProjectId == created.ContentProjectId && r.Type == AssetReferenceType.Character)
            .ToList();
        Assert.Equal(2, characterRefs.Count);

        // Milo's existing (own) reference is untouched, not duplicated.
        var miloRefs = characterRefs.Where(r => r.Label == "Milo").ToList();
        Assert.Single(miloRefs);
        Assert.Equal("refs/milo.png", miloRefs[0].ImagePath);

        var mimiRef = Assert.Single(characterRefs, r => r.Label == "Mimi");
        Assert.Equal(AssetReferenceStatus.Approved, mimiRef.Status);
        Assert.Equal("stories/story/characters/mimi/ref.png", mimiRef.ImagePath);
    }

    [Fact]
    public async Task Sync_throws_when_the_episode_has_no_linked_video_yet()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.SyncStoryReferencesAsync(story.Id, episode.Id));
    }

    [Fact]
    public async Task Sync_throws_and_clears_the_stale_link_when_the_linked_ContentProject_no_longer_exists()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(created);
        var deadProjectId = created!.ContentProjectId;

        // Simulate ContentProjectsController.Delete: the project is gone, but
        // the episode's ContentProjectId is left dangling.
        f.ProjectRepo.Projects.Remove(deadProjectId);

        AttachApprovedCharacter(f.StoryRepo, story);

        var rowCountBefore = f.ReferenceRepo.Rows.Count(r => r.ContentProjectId == deadProjectId);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.SyncStoryReferencesAsync(story.Id, episode.Id));

        // No AssetReference rows were silently written against the dead ContentProjectId.
        Assert.Equal(rowCountBefore, f.ReferenceRepo.Rows.Count(r => r.ContentProjectId == deadProjectId));

        // The stale link was cleared so a subsequent "Create Video" call self-heals.
        Assert.Null(episode.ContentProjectId);
    }

    [Fact]
    public async Task Sync_returns_null_for_unknown_story_or_episode()
    {
        var f = Build();

        var unknownStory = await f.Service.SyncStoryReferencesAsync(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(unknownStory);

        var story = Story.Create("My Series", null, null, null, null);
        f.StoryRepo.Stories[story.Id] = story;

        var unknownEpisode = await f.Service.SyncStoryReferencesAsync(story.Id, Guid.NewGuid());
        Assert.Null(unknownEpisode);
    }

    [Fact]
    public async Task Sync_reports_no_synced_types_when_nothing_needs_syncing()
    {
        var f = Build();
        var (story, episode) = SeedStoryWithScriptedEpisode(f.StoryRepo);

        // No Story-level approved images at all, so nothing to sync.
        var created = await f.Service.CreateOrOpenVideoAsync(story.Id, episode.Id);
        Assert.NotNull(created);

        var rowCountBefore = f.ReferenceRepo.Rows.Count;

        var result = await f.Service.SyncStoryReferencesAsync(story.Id, episode.Id);

        Assert.NotNull(result);
        Assert.Empty(result!.SyncedLabels);
        Assert.Equal(rowCountBefore, f.ReferenceRepo.Rows.Count);
    }
}
