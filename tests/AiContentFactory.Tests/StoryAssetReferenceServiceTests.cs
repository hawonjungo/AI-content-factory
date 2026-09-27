using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="StoryAssetReferenceService"/>: the Story-level Character/
/// Location reference image, generated once (deterministic prompt, no LLM
/// call) and approved before <see cref="StoryVideoLinkService"/> ever reuses
/// it across episodes. Fakes only - no real image provider is ever called.
/// </summary>
public class StoryAssetReferenceServiceTests
{
    /// <summary>Counts EnsureBudget calls; optionally throws like the real tracker at the monthly limit.</summary>
    private sealed class BudgetUsageTracker : IAiUsageTracker
    {
        public List<RecordUsageInput> Records { get; } = new();
        public int EnsureCalls { get; private set; }
        public bool Exhausted { get; set; }

        public Task RecordAsync(RecordUsageInput input, CancellationToken cancellationToken = default)
        {
            Records.Add(input);
            return Task.CompletedTask;
        }

        public Task<decimal> GetCurrentMonthSpendAsync(CancellationToken cancellationToken = default) => Task.FromResult(0m);

        public Task EnsureBudgetAvailableAsync(CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return Exhausted ? throw new BudgetExceededException(10m, 10m) : Task.CompletedTask;
        }
    }

    /// <summary>Only EnsureAvailableAsync is used by the service; everything else is unsupported on purpose.</summary>
    private sealed class DailyCreditLedger : ICreditLedger
    {
        public List<int> EnsuredCredits { get; } = new();
        public bool Exhausted { get; set; }

        public Task EnsureAvailableAsync(int requiredCredits, CancellationToken cancellationToken = default)
        {
            EnsuredCredits.Add(requiredCredits);
            return Exhausted ? throw new CreditBudgetExceededException(requiredCredits, 0, 10) : Task.CompletedTask;
        }

        public Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CreditUsageSummary> GetDailyUsageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerationAttempt> ReserveAsync(CreditReservationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CommitAsync(Guid attemptId, int actualCredits, Guid? assetId = null, double? audioDurationSeconds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GenerationAttempt> RecordExternalCompletionAsync(CreditReservationRequest request, int actualCredits, Guid? assetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkValidatedAsync(Guid attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task FailAsync(Guid attemptId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record Fixture(
        StoryAssetReferenceService Service,
        FakeImageGenerationProvider Provider,
        FakeFileStorage Storage,
        BudgetUsageTracker Usage,
        DailyCreditLedger Ledger,
        FakeStoryRepository Repo,
        StoryReferenceGenerationGate Gate,
        FakeAssetReferenceRepository AssetRefs);

    /// <summary>Blocks until the caller-supplied token is cancelled - a stand-in for a hung ffprobe.</summary>
    private sealed class HangingMediaProbe : IMediaProbe
    {
        public async Task<MediaInfo> ProbeAsync(string absolutePath, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return MediaInfo.Unreadable("unreachable");
        }
    }

    private static Fixture Build(MediaInfo? probeResult = null, int imageCredits = 3, IMediaProbe? customProbe = null, TimeSpan? probeTimeout = null)
    {
        var repo = new FakeStoryRepository();
        var assetRefs = new FakeAssetReferenceRepository();
        repo.AssetReferences = assetRefs;
        var provider = new FakeImageGenerationProvider();
        var storage = new FakeFileStorage();
        var usage = new BudgetUsageTracker();
        var ledger = new DailyCreditLedger();
        var gate = new StoryReferenceGenerationGate();
        var probe = customProbe ?? new FakeMediaProbe(probeResult ?? FakeMediaProbe.GoodInfo(duration: 0.04, w: 1024, h: 1536, fps: 25));
        var service = new StoryAssetReferenceService(
            repo, provider, storage, probe, usage, ledger, gate,
            Options.Create(new PricingOptions()),
            Options.Create(new CreditCostOptions { ImageCredits = imageCredits }),
            NullLogger<StoryAssetReferenceService>.Instance)
        {
            ProbeTimeout = probeTimeout ?? TimeSpan.FromSeconds(15)
        };
        return new Fixture(service, provider, storage, usage, ledger, repo, gate, assetRefs);
    }

    private static (Story Story, StoryCharacter Character) SeedCharacter(
        FakeStoryRepository repo,
        StoryBible? bible = null,
        string? description = "A witty fox detective.",
        string? visualDescription = "Orange fur, wears a blue trench coat.",
        string? species = null)
    {
        var story = Story.Create("My Series", "A premise", "drama", "en", "9:16");
        if (bible is not null)
        {
            story.SetBible(bible);
        }
        var character = StoryCharacter.Create(story.Id, "Nova", description, visualDescription, species: species);
        story.AttachCharacter(character);
        repo.Stories[story.Id] = story;
        repo.Characters[character.Id] = character;
        return (story, character);
    }

    private static (Story Story, StoryLocation Location) SeedLocation(FakeStoryRepository repo)
    {
        var story = Story.Create("My Series", "A premise", "drama", "en", "9:16");
        var location = StoryLocation.Create(story.Id, "Neon Alley", "A back alley in the city.", "Rain-slicked pavement, neon signage.");
        story.AttachLocation(location);
        repo.Stories[story.Id] = story;
        repo.Locations[location.Id] = location;
        return (story, location);
    }

    private static Stream Png() => new MemoryStream(PngBytes.OnePixel());

    // ---- Character: generate happy path ----

    [Fact]
    public async Task Generate_character_reference_stores_a_Generated_candidate_and_records_usage()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        var result = await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        Assert.NotNull(result);
        Assert.Equal(AssetReferenceStatus.Generated.ToString(), result!.Status);
        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);
        Assert.False(string.IsNullOrWhiteSpace(character.ReferenceImagePath));
        Assert.Contains(character.ReferenceImagePath!, f.Storage.Files.Keys);
        Assert.StartsWith($"stories/{story.Id}/characters/{character.Id}/", character.ReferenceImagePath);
        Assert.Equal(ReferenceImageSource.Generated, character.ReferenceImageSource);
        Assert.Equal(f.Provider.LastRequest!.Prompt, character.ReferenceImagePrompt);
        Assert.False(result.HasPending);
        Assert.Equal("Generated", result.Source);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Single(f.Usage.Records);
        Assert.Equal("story_character_reference_image_generation", f.Usage.Records[0].Operation);
        Assert.Null(f.Usage.Records[0].ContentProjectId); // story-level, not a ContentProject spend
    }

    [Fact]
    public async Task Character_prompt_is_the_composers_InApp_output_built_from_the_characters_own_fields()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, species: "red fox");
        character.UpdateCanonicalProfile(CharacterKind.Animal, null, "a blue collar", "a white tail tip");
        story.SetStylePreset("pixar-3d");

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var expected = CharacterReferencePromptComposer.Compose(
            CharacterReferenceSpec.FromCharacter(character, PresetCatalog.FindStyle("pixar-3d")), ReferencePromptTarget.InApp);
        Assert.Equal(expected.Prompt, f.Provider.LastRequest!.Prompt);
        Assert.Equal(expected.NegativePrompt, f.Provider.LastRequest.NegativePrompt);
        Assert.Contains("Orange fur, wears a blue trench coat", f.Provider.LastRequest.Prompt);
        Assert.Contains("a blue collar", f.Provider.LastRequest.Prompt);
        Assert.Contains("a white tail tip", f.Provider.LastRequest.Prompt);
        Assert.Null(f.Provider.LastRequest.ReferenceImages);
    }

    [Fact]
    public async Task Character_prompt_never_carries_bible_rules_or_the_bible_tone()
    {
        var f = Build();
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: new[]
            {
                "Scenes are always set in a rain-slicked alley at night",
                "The chase happens around Neon Alley",
                "Mimi always wears a pink beret",
                "Nova must always be rendered as a white fox with blue eyes",
                "the sidekick always carries a lantern"
            },
            tone: "noir film look", recurringElements: null, storyConstraints: null, storyArc: null);
        var (story, character) = SeedCharacter(f.Repo, bible);

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var prompt = f.Provider.LastRequest!.Prompt;
        foreach (var rule in bible.VisualConsistencyRules)
        {
            Assert.DoesNotContain(rule, prompt);
        }
        Assert.DoesNotContain("Mimi", prompt);
        Assert.DoesNotContain("lantern", prompt);
        Assert.DoesNotContain("white fox with blue eyes", prompt);
        // No story style -> no style line at all, and no fallback to the Bible tone any more.
        Assert.DoesNotContain("noir film look", prompt);
        Assert.DoesNotContain("Series visual style", prompt);
        Assert.DoesNotContain("Rendering style", prompt);
        Assert.Contains("Orange fur, wears a blue trench coat", prompt);
    }

    [Fact]
    public async Task Character_prompt_is_a_full_body_neutral_pose_on_a_seamless_solid_white_background()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var prompt = f.Provider.LastRequest!.Prompt;
        Assert.Contains("Seamless solid white background", prompt);
        Assert.DoesNotContain("grey", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gray", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Full-body character reference", prompt);
        Assert.Contains("neutral, relaxed standing pose", prompt);
        Assert.Contains("neutral expression", prompt);
        Assert.Contains("Soft, even studio lighting", prompt);
        Assert.DoesNotContain("8K", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key moment", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Character_prompt_uses_the_stories_look_only_style_wording_exactly_once()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        story.SetStylePreset("pixar-3d");

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var pixar = PresetCatalog.FindStyle("pixar-3d")!;
        var request = f.Provider.LastRequest!;
        Assert.Contains("Rendering style (look only): " + pixar.ReferenceLookGuidance, request.Prompt);
        Assert.Equal(1, CountOccurrences(request.Prompt, pixar.ReferenceLookGuidance.TrimEnd('.')));
        Assert.DoesNotContain(pixar.VisualStyleGuidance, request.Prompt);
        // The style's look-only exclusions travel in the negative prompt (not its scene-oriented ones), once.
        Assert.Contains("photorealistic", request.NegativePrompt!);
        Assert.DoesNotContain("harsh shadows", request.NegativePrompt!);
        Assert.DoesNotContain(pixar.ReferenceLookGuidance, request.NegativePrompt!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-style-that-was-removed-from-the-catalog")]
    public async Task Character_prompt_without_a_usable_story_style_has_no_style_line_and_no_tone_fallback(string? styleId)
    {
        var f = Build();
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: null,
            tone: "warm watercolor", recurringElements: null, storyConstraints: null, storyArc: null);
        var (story, character) = SeedCharacter(f.Repo, bible);
        story.SetStylePreset(styleId);

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var request = f.Provider.LastRequest!;
        Assert.DoesNotContain("warm watercolor", request.Prompt);
        Assert.DoesNotContain("Series visual style", request.Prompt);
        Assert.DoesNotContain("Rendering style", request.Prompt);
        // PresetCatalog.ResolveStyle would have silently substituted the dark house style.
        Assert.DoesNotContain("35mm film grain", request.Prompt);
        Assert.NotNull(request.NegativePrompt);
    }

    [Theory]
    [InlineData("rain-soaked neon city at night")]
    [InlineData("moody lighting, deep shadows")]
    [InlineData("warm watercolor")]
    public async Task A_bible_tone_never_lands_on_the_character_sheet_or_the_location_plate(string tone)
    {
        var f = Build();
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: null,
            tone: tone, recurringElements: null, storyConstraints: null, storyArc: null);
        var (story, character) = SeedCharacter(f.Repo, bible);
        var location = StoryLocation.Create(story.Id, "Neon Alley", "A back alley.", "Rain-slicked pavement.");
        story.AttachLocation(location);
        f.Repo.Locations[location.Id] = location;

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);
        Assert.DoesNotContain(tone, f.Provider.LastRequest!.Prompt);
        Assert.DoesNotContain("Series visual style", f.Provider.LastRequest.Prompt);

        await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);
        Assert.DoesNotContain(tone, f.Provider.LastRequest!.Prompt);
        Assert.DoesNotContain("Series visual style", f.Provider.LastRequest.Prompt);
    }

    [Fact]
    public async Task Character_negative_prompt_excludes_backgrounds_scenery_and_pose_failures_and_keeps_the_quality_safety_net()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        story.SetStylePreset("anime");

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var negative = f.Provider.LastRequest!.NegativePrompt;
        Assert.False(string.IsNullOrWhiteSpace(negative));
        var terms = negative!.Split(',', StringSplitOptions.TrimEntries);
        foreach (var expected in new[]
                 {
                     "background scenery", "environment", "landscape", "room interior", "furniture", "props", "other characters", "text", "watermark",
                     "cropped", "cut off", "sitting", "crouching", "crossed arms", "leaning", "dynamic pose", "extreme perspective", "side view", "food"
                 })
        {
            Assert.Contains(expected, terms);
        }
        Assert.Contains("3d render", terms); // the anime style's own look-only exclusion
        Assert.Contains("blurry", terms); // quality net
        Assert.Equal(terms.Length, terms.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // merged without duplicates
        // The Character negative must never block the character itself.
        Assert.DoesNotContain("people", terms);
        Assert.DoesNotContain("characters", terms);
    }

    // ---- Character: generation guard rails (paid call) ----

    [Fact]
    public async Task Generation_is_refused_with_no_provider_call_when_the_character_has_neither_description_nor_species()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: null, visualDescription: null, species: null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));

        Assert.Contains("Nova", ex.Message);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Usage.Records);
        Assert.Equal(0, f.Usage.EnsureCalls); // refused before even touching the budget
        Assert.Empty(f.Storage.Files);
        Assert.Equal(AssetReferenceStatus.Pending, character.ReferenceImageStatus);
    }

    [Fact]
    public async Task A_species_alone_is_enough_to_generate()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: null, visualDescription: null, species: "snow leopard");

        var result = await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        Assert.NotNull(result);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Contains("snow leopard", f.Provider.LastRequest!.Prompt);
    }

    [Fact]
    public async Task A_role_description_without_an_appearance_does_not_satisfy_the_paid_gate()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: "A brave, sarcastic detective.", visualDescription: null, species: null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));

        Assert.Contains("Ngoại hình", ex.Message);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Equal(0, f.Usage.EnsureCalls);
    }

    [Fact]
    public async Task The_description_is_never_presented_as_the_canonical_appearance()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: "A brave, sarcastic detective.", visualDescription: "Orange fur, blue coat.", species: null);

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        Assert.Contains("Canonical appearance: Orange fur, blue coat.", f.Provider.LastRequest!.Prompt);
        Assert.DoesNotContain("sarcastic", f.Provider.LastRequest.Prompt);
    }

    [Fact]
    public async Task A_stale_species_on_a_Human_does_not_satisfy_the_paid_gate()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: null, visualDescription: null, species: "tabby cat");
        character.UpdateCanonicalProfile(CharacterKind.Human, null, null, null);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));

        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task A_human_with_an_appearance_can_generate()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: null, visualDescription: "Tall, silver hair.", species: "stale species");
        character.UpdateCanonicalProfile(CharacterKind.Human, null, null, null);

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        Assert.Equal(1, f.Provider.Calls);
        Assert.DoesNotContain("stale species", f.Provider.LastRequest!.Prompt);
    }

    [Fact]
    public async Task The_monthly_budget_guard_runs_before_the_paid_call_and_blocks_it()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        f.Usage.Exhausted = true;

        await Assert.ThrowsAsync<BudgetExceededException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));

        Assert.Equal(1, f.Usage.EnsureCalls);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Usage.Records);
        Assert.Equal(AssetReferenceStatus.Pending, character.ReferenceImageStatus);
    }

    [Fact]
    public async Task The_daily_credit_guard_uses_the_image_credit_cost_and_blocks_the_paid_call()
    {
        var f = Build(imageCredits: 4);
        var (story, character) = SeedCharacter(f.Repo);

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);
        Assert.Equal(new[] { 4 }, f.Ledger.EnsuredCredits);

        f.Ledger.Exhausted = true;
        var callsBefore = f.Provider.Calls;
        await Assert.ThrowsAsync<CreditBudgetExceededException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));
        Assert.Equal(callsBefore, f.Provider.Calls);
    }

    [Fact]
    public async Task A_provider_failure_is_rethrown_and_leaves_the_character_Pending_with_no_usage_recorded()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        f.Provider.Throw = new HttpRequestException("image API 500");

        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));

        Assert.Equal(AssetReferenceStatus.Pending, character.ReferenceImageStatus);
        Assert.Empty(f.Usage.Records);
    }

    [Fact]
    public async Task Unknown_story_or_character_returns_null()
    {
        var f = Build();
        var (story, _) = SeedCharacter(f.Repo);

        Assert.Null(await f.Service.GenerateCharacterReferenceAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Null(await f.Service.GenerateCharacterReferenceAsync(story.Id, Guid.NewGuid()));
        Assert.Null(await f.Service.UploadCharacterReferenceAsync(story.Id, Guid.NewGuid(), "a.png", Png()));
        Assert.Null(await f.Service.GetCharacterReferencePromptAsync(story.Id, Guid.NewGuid(), ReferencePromptTarget.Generic));
        Assert.Null(await f.Service.DiscardPendingCharacterReferenceAsync(story.Id, Guid.NewGuid()));
        Assert.Null(await f.Service.ApproveCharacterReferenceAsync(story.Id, Guid.NewGuid()));
        Assert.Equal(0, f.Provider.Calls);
    }

    // ---- Character: candidate / approve / discard workflow ----

    private static async Task<StoryCharacter> ApprovedCharacterAsync(Fixture f, string approvedPath = "stories/x/approved.png")
    {
        var (story, character) = SeedCharacter(f.Repo);
        f.Storage.Files[approvedPath] = new byte[] { 1, 2, 3 };
        character.SetReferenceCandidate(approvedPath, "approved prompt", "gemini", ReferenceImageSource.Generated);
        await f.Service.ApproveCharacterReferenceAsync(story.Id, character.Id);
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        return character;
    }

    [Fact]
    public async Task Regenerating_an_approved_reference_never_overwrites_it_and_writes_a_pending_candidate()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        var storyId = character.StoryId;

        var result = await f.Service.GenerateCharacterReferenceAsync(storyId, character.Id);

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("stories/x/approved.png", character.ReferenceImagePath);
        Assert.Equal("approved prompt", character.ReferenceImagePrompt);
        Assert.True(character.HasPendingReferenceImage);
        Assert.Contains(character.PendingReferenceImagePath!, f.Storage.Files.Keys);
        Assert.Equal(ReferenceImageSource.Generated, character.PendingReferenceImageSource);
        Assert.True(result!.HasPending);
        Assert.Equal("Approved", result.Status);
        Assert.Equal("stories/x/approved.png", result.ImagePath);
        Assert.Equal("Generated", result.PendingSource);
        Assert.Contains("stories/x/approved.png", f.Storage.Files.Keys); // the approved file is untouched
    }

    [Fact]
    public async Task A_second_regeneration_replaces_the_pending_candidate_and_removes_its_orphaned_file()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);
        var firstPending = character.PendingReferenceImagePath!;

        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);

        Assert.NotEqual(firstPending, character.PendingReferenceImagePath);
        Assert.DoesNotContain(firstPending, f.Storage.Files.Keys);
        Assert.Contains(character.PendingReferenceImagePath!, f.Storage.Files.Keys);
        Assert.Equal("stories/x/approved.png", character.ReferenceImagePath);
    }

    [Fact]
    public async Task Approving_a_pending_candidate_over_an_approved_image_requires_confirmReplace()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);
        var pending = character.PendingReferenceImagePath;

        var ex = await Assert.ThrowsAsync<ReferenceReplaceConfirmationRequiredException>(
            () => f.Service.ApproveCharacterReferenceAsync(character.StoryId, character.Id));
        Assert.IsAssignableFrom<DomainException>(ex);
        await Assert.ThrowsAsync<ReferenceReplaceConfirmationRequiredException>(
            () => f.Service.ApproveCharacterReferenceAsync(character.StoryId, character.Id, confirmReplace: false));

        // Nothing changed.
        Assert.Equal("stories/x/approved.png", character.ReferenceImagePath);
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal(pending, character.PendingReferenceImagePath);
    }

    [Fact]
    public async Task Confirmed_replace_promotes_the_candidate_clears_pending_and_keeps_the_old_file_for_existing_episodes()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);
        var pending = character.PendingReferenceImagePath!;
        var pendingPrompt = character.PendingReferenceImagePrompt;

        var result = await f.Service.ApproveCharacterReferenceAsync(character.StoryId, character.Id, confirmReplace: true);

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal(pending, character.ReferenceImagePath);
        Assert.Equal(pendingPrompt, character.ReferenceImagePrompt);
        Assert.False(character.HasPendingReferenceImage);
        Assert.False(result!.HasPending);
        Assert.Equal("Approved", result.Status);
        Assert.Contains("stories/x/approved.png", f.Storage.Files.Keys);
    }

    [Fact]
    public async Task Approving_a_freshly_generated_image_needs_no_confirmation_and_canonical_fields_survive()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, species: "red fox");
        character.UpdateCanonicalProfile(CharacterKind.Animal, null, "blue collar", "white tail tip");
        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var result = await f.Service.ApproveCharacterReferenceAsync(story.Id, character.Id);

        Assert.Equal("Approved", result!.Status);
        Assert.Equal(CharacterKind.Animal, character.Kind);
        Assert.Equal("red fox", character.Species);
        Assert.Equal("blue collar", character.ClothingAndAccessories);
        Assert.Equal("white tail tip", character.DistinctiveFeatures);
    }

    [Fact]
    public async Task Approve_character_reference_without_generating_first_throws_and_propagates_cleanly()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.ApproveCharacterReferenceAsync(story.Id, character.Id));
        Assert.Equal(AssetReferenceStatus.Pending, character.ReferenceImageStatus);
    }

    [Fact]
    public async Task Discarding_the_pending_candidate_keeps_the_approved_image_and_deletes_only_the_candidate_file()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);
        var pending = character.PendingReferenceImagePath!;

        var result = await f.Service.DiscardPendingCharacterReferenceAsync(character.StoryId, character.Id);

        Assert.False(character.HasPendingReferenceImage);
        Assert.False(result!.HasPending);
        Assert.Equal("Approved", result.Status);
        Assert.Equal("stories/x/approved.png", result.ImagePath);
        Assert.DoesNotContain(pending, f.Storage.Files.Keys);
        Assert.Contains("stories/x/approved.png", f.Storage.Files.Keys);
    }

    [Fact]
    public async Task Discarding_when_there_is_no_pending_candidate_is_a_harmless_no_op()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);

        var result = await f.Service.DiscardPendingCharacterReferenceAsync(character.StoryId, character.Id);

        Assert.NotNull(result);
        Assert.Equal("Approved", result!.Status);
        Assert.Contains("stories/x/approved.png", f.Storage.Files.Keys);
    }

    // ---- Character: upload ----

    [Fact]
    public async Task Upload_stores_an_Uploaded_candidate_with_a_server_generated_key_and_records_no_cost()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        var result = await f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "..\\..\\My Secret Nova.PNG", Png());

        Assert.NotNull(result);
        Assert.Equal("Generated", result!.Status); // an upload is a candidate too - approval is a separate step
        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);
        Assert.Equal(ReferenceImageSource.Uploaded, character.ReferenceImageSource);
        Assert.Equal("Uploaded", result.Source);
        Assert.Equal(StoryAssetReferenceService.UploadProviderLabel, character.ReferenceImageProvider);
        Assert.Equal("user-upload", result.Provider);
        Assert.Null(character.ReferenceImagePrompt);
        // The client's file name never reaches the storage key.
        Assert.StartsWith($"stories/{story.Id}/characters/{character.Id}/reference-upload-", character.ReferenceImagePath);
        Assert.EndsWith(".png", character.ReferenceImagePath);
        Assert.DoesNotContain("Secret", character.ReferenceImagePath);
        Assert.DoesNotContain("..", character.ReferenceImagePath);
        Assert.Equal(PngBytes.OnePixel(), f.Storage.Files[character.ReferenceImagePath!]);
        // Free: no provider call, no usage record, no credit check.
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Usage.Records);
        Assert.Equal(0, f.Usage.EnsureCalls);
        Assert.Empty(f.Ledger.EnsuredCredits);
    }

    [Fact]
    public async Task Upload_over_an_approved_image_becomes_a_pending_candidate_and_never_replaces_it()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);

        var result = await f.Service.UploadCharacterReferenceAsync(character.StoryId, character.Id, "new.jpg", new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 1, 2, 3 }));

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("stories/x/approved.png", character.ReferenceImagePath);
        Assert.True(character.HasPendingReferenceImage);
        Assert.EndsWith(".jpg", character.PendingReferenceImagePath);
        Assert.Equal(ReferenceImageSource.Uploaded, character.PendingReferenceImageSource);
        Assert.Equal("user-upload", character.PendingReferenceImageProvider);
        Assert.True(result!.HasPending);
        Assert.Equal("Uploaded", result.PendingSource);
        Assert.Equal("Generated", result.Source);
        Assert.Empty(f.Usage.Records);
    }

    [Fact]
    public async Task The_stored_extension_follows_the_real_content_not_the_clients_claim()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        await f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "photo.png", new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 1 }));

        Assert.EndsWith(".jpg", character.ReferenceImagePath);
    }

    [Theory]
    [InlineData("photo.webp")]
    [InlineData("photo.png")] // a WebP hiding behind an allowed extension is caught by its signature
    public async Task WebP_is_not_accepted_only_PNG_and_JPEG_are(string fileName)
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        var webp = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 4, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 1 };

        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, fileName, new MemoryStream(webp)));

        Assert.Empty(f.Storage.Files);
        Assert.Null(character.ReferenceImagePath);
    }

    [Theory]
    [InlineData("nova.gif")]
    [InlineData("nova.exe")]
    [InlineData("nova.png.exe")]
    [InlineData("nova")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Upload_rejects_a_disallowed_extension_and_stores_nothing(string? fileName)
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, fileName, Png()));

        Assert.Empty(f.Storage.Files);
        Assert.False(character.HasPendingReferenceImage);
        Assert.Null(character.ReferenceImagePath);
    }

    [Fact]
    public async Task Upload_rejects_an_empty_file()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", new MemoryStream()));

        Assert.Empty(f.Storage.Files);
    }

    [Fact]
    public async Task Upload_rejects_an_oversized_file_without_storing_it()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        var big = new byte[StoryAssetReferenceService.MaxUploadBytes + 1];
        PngBytes.OnePixel().CopyTo(big, 0);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", new MemoryStream(big)));

        Assert.Empty(f.Storage.Files);
        Assert.Null(character.ReferenceImagePath);
    }

    [Fact]
    public async Task Upload_accepts_a_file_exactly_at_the_size_limit()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        var exact = new byte[StoryAssetReferenceService.MaxUploadBytes];
        PngBytes.OnePixel().CopyTo(exact, 0);

        var result = await f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", new MemoryStream(exact));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Upload_rejects_content_that_is_not_an_image_even_with_an_image_extension()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        await Assert.ThrowsAsync<DomainException>(() =>
            f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", new MemoryStream(System.Text.Encoding.UTF8.GetBytes("MZ definitely not an image"))));

        Assert.Empty(f.Storage.Files);
    }

    [Fact]
    public async Task Upload_rejects_an_unreadable_image_and_removes_the_stored_file()
    {
        var f = Build(probeResult: MediaInfo.Unreadable("ffprobe failed for C:/data/stories/secret.png: Invalid data found when processing input"));
        var (story, character) = SeedCharacter(f.Repo);

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png()));

        // A generic Vietnamese message: the probe's own text (which can carry filesystem paths) is never returned.
        Assert.Contains("Không đọc được file ảnh", ex.Message);
        Assert.DoesNotContain("ffprobe", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", ex.Message);
        Assert.DoesNotContain("C:", ex.Message);
        Assert.DoesNotContain("Invalid data", ex.Message);
        Assert.Empty(f.Storage.Files);
        Assert.False(character.HasPendingReferenceImage);
        Assert.Null(character.ReferenceImagePath);
        Assert.Empty(f.Usage.Records);
    }

    [Fact]
    public async Task Upload_rejects_an_image_with_no_dimensions()
    {
        var f = Build(probeResult: FakeMediaProbe.GoodInfo(duration: 0.04, w: 0, h: 0));
        var (story, character) = SeedCharacter(f.Repo);

        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png()));

        Assert.Empty(f.Storage.Files);
    }

    // ---- Character: composed reference prompt endpoint (no AI, no cost) ----

    [Fact]
    public async Task The_reference_prompt_is_returned_even_for_a_character_that_is_not_generation_ready_without_any_cost()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, description: null, visualDescription: null, species: null);

        var result = await f.Service.GetCharacterReferencePromptAsync(story.Id, character.Id, ReferencePromptTarget.Midjourney);

        Assert.NotNull(result);
        Assert.Equal("midjourney", result!.Target);
        Assert.Contains("Nova", result.Prompt);
        Assert.Contains("--ar 2:3 --no ", result.Prompt);
        Assert.Null(result.NegativePrompt);
        Assert.Contains("appearance", result.MissingFields);
        Assert.Contains("species", result.MissingFields);
        Assert.NotEmpty(result.Notes);
        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Usage.Records);
        Assert.Equal(0, f.Usage.EnsureCalls);
    }

    [Fact]
    public async Task The_reference_prompt_uses_the_stories_style_and_the_characters_opt_in()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo, species: "fox");
        character.UpdateCanonicalProfile(CharacterKind.AnthropomorphicAnimal, null, null, null);
        story.SetStylePreset("pixar-3d");

        var inApp = await f.Service.GetCharacterReferencePromptAsync(story.Id, character.Id, ReferencePromptTarget.InApp);

        Assert.Equal("inapp", inApp!.Target);
        Assert.Contains("anthropomorphic fox", inApp.Prompt);
        Assert.Contains("Rendering style (look only): " + PresetCatalog.FindStyle("pixar-3d")!.ReferenceLookGuidance.TrimEnd('.'), inApp.Prompt);
        Assert.False(string.IsNullOrWhiteSpace(inApp.NegativePrompt));
    }

    // ---- Every preset: reference prompts only ever use the look-only fields ----

    // Words that describe pose / expression / lighting / framing / camera / setting - none may appear in a
    // preset's reference look fields, since those fight the neutral plain-background / empty-plate staging.
    private static readonly string[] DeniedWords =
    {
        "pov", "selfie", "holding", "holds", "smile", "smiling", "expression", "expressive", "expressiveness", "pose", "posed", "posing",
        "lighting", "light", "lit", "daylight", "illumination", "shadow", "shadows", "rim", "backlit",
        "shot", "framed", "framing", "wide", "close-up", "angle", "camera", "handheld", "lens", "depth", "anamorphic",
        "walks", "walking", "stands", "standing", "upright", "waving", "gripping", "wearing", "eyes", "mouth", "paws", "hands",
        "background", "backdrop", "city", "street", "room", "night", "asphalt", "fog", "architecture", "rural", "scene", "setting"
    };

    public static IEnumerable<object[]> AllStyleIds() => PresetCatalog.Styles.Select(s => new object[] { s.Id });

    [Theory]
    [MemberData(nameof(AllStyleIds))]
    public void Every_preset_has_look_only_reference_fields_free_of_pose_expression_lighting_framing_and_setting_words(string styleId)
    {
        var style = PresetCatalog.FindStyle(styleId)!;

        Assert.False(string.IsNullOrWhiteSpace(style.ReferenceLookGuidance));
        Assert.False(string.IsNullOrWhiteSpace(style.ReferenceNegativePrompt));
        foreach (var field in new[] { style.ReferenceLookGuidance, style.ReferenceNegativePrompt })
        {
            var words = System.Text.RegularExpressions.Regex.Matches(field.ToLowerInvariant(), @"[a-z0-9]+(?:-[a-z0-9]+)*")
                .Select(m => m.Value)
                .ToHashSet();
            foreach (var denied in DeniedWords)
            {
                Assert.False(words.Contains(denied), $"Preset '{styleId}' reference field contains scene/pose word '{denied}': {field}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllStyleIds))]
    public async Task Story_character_and_location_prompts_use_only_the_look_only_fields_never_the_full_style_text(string styleId)
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        var location = StoryLocation.Create(story.Id, "Neon Alley", "A back alley.", "Rain-slicked pavement.");
        story.AttachLocation(location);
        f.Repo.Locations[location.Id] = location;
        story.SetStylePreset(styleId);
        var style = PresetCatalog.FindStyle(styleId)!;

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);
        var characterRequest = f.Provider.LastRequest!;
        await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);
        var locationRequest = f.Provider.LastRequest!;

        foreach (var request in new[] { characterRequest, locationRequest })
        {
            Assert.Contains(style.ReferenceLookGuidance.TrimEnd('.'), request.Prompt);
            Assert.DoesNotContain(style.VisualStyleGuidance, request.Prompt);
            Assert.DoesNotContain(style.VisualStyleGuidance.TrimEnd('.'), request.Prompt);
            Assert.NotNull(request.NegativePrompt);
            if (style.NegativePrompt is not null)
            {
                Assert.DoesNotContain(style.NegativePrompt, request.NegativePrompt!);
            }
            foreach (var term in style.ReferenceNegativePrompt.Split(',', StringSplitOptions.TrimEntries))
            {
                Assert.Contains(term, request.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries));
            }
        }
    }

    [Fact]
    public async Task The_cat_travel_preset_no_longer_leaks_pose_expression_or_framing_into_the_character_sheet()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        story.SetStylePreset("cat-travel-stylized-realism");

        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        var request = f.Provider.LastRequest!;
        foreach (var leaked in new[] { "POV", "selfie", "holding a camera", "big bright smile", "walks upright", "handheld" })
        {
            Assert.DoesNotContain(leaked, request.Prompt, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var leaked in new[] { "stiff static pose", "bored expression", "closed mouth", "flat documentary staging" })
        {
            Assert.DoesNotContain(leaked, request.NegativePrompt!, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Location: unchanged behaviour except the dropped tone fallback ----

    [Fact]
    public async Task Location_prompt_ignores_bible_rules_entirely_but_keeps_the_locations_own_fields()
    {
        var f = Build();
        var bible = StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: new[]
            {
                "Neon Alley must always be rendered as a sunny daytime plaza",
                "Nova always wears the blue coat"
            },
            tone: "noir, moody lighting", recurringElements: null, storyConstraints: null, storyArc: null);
        var story = Story.Create("My Series", "A premise", "drama", "en", "9:16");
        story.SetBible(bible);
        var location = StoryLocation.Create(story.Id, "Neon Alley", "A back alley in the city.", "Rain-slicked pavement, neon signage.");
        story.AttachLocation(location);
        f.Repo.Stories[story.Id] = story;
        f.Repo.Locations[location.Id] = location;

        await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);

        var prompt = f.Provider.LastRequest!.Prompt;
        Assert.Contains("Rain-slicked pavement, neon signage.", prompt);
        Assert.DoesNotContain("sunny daytime plaza", prompt);
        Assert.DoesNotContain("Nova", prompt); // a person must not sneak into the empty plate
        Assert.DoesNotContain("Visual consistency rules", prompt);
        Assert.DoesNotContain("noir, moody lighting", prompt); // no Bible-tone fallback any more
    }

    [Fact]
    public async Task Location_prompt_is_an_empty_establishing_plate_with_no_people_and_a_people_excluding_negative()
    {
        var f = Build();
        var (story, location) = SeedLocation(f.Repo);
        story.SetStylePreset("neon-cyberpunk");

        await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);

        var request = f.Provider.LastRequest!;
        Assert.Contains("Neon Alley", request.Prompt);
        Assert.Contains("Rain-slicked pavement, neon signage.", request.Prompt);
        Assert.Contains("Empty establishing plate", request.Prompt);
        Assert.Contains("NO people, no characters, no figures, no silhouettes", request.Prompt);
        Assert.Contains("magenta and cyan color grade", request.Prompt); // story style, look-only
        Assert.DoesNotContain("neon signage reflections on wet asphalt", request.Prompt); // scene language of the full guidance
        Assert.EndsWith("Empty scene with nobody in it.", request.Prompt);

        var terms = request.NegativePrompt!.Split(',', StringSplitOptions.TrimEntries);
        foreach (var expected in new[] { "people", "person", "human", "characters", "figures", "silhouettes", "faces", "crowd", "animals as characters" })
        {
            Assert.Contains(expected, terms);
        }
        Assert.Contains("muted colors", terms); // the style's own look-only exclusion
        Assert.DoesNotContain("daylight", terms); // lighting term of the full negative is not used
        Assert.Contains("blurry", terms);
        Assert.DoesNotContain("background scenery", terms); // that is the Character sheet's exclusion
        Assert.DoesNotContain("sitting", terms); // pose exclusions belong to the Character sheet only
    }

    [Fact]
    public async Task Location_prompt_without_a_story_style_has_no_style_line_and_no_bible_tone()
    {
        var f = Build();
        var (story, location) = SeedLocation(f.Repo);
        story.SetBible(StoryBible.Create(
            premise: "p", worldRules: null, characterDefinitions: null, characterRelationships: null,
            visualConsistencyRules: null,
            tone: "noir, moody lighting", recurringElements: null, storyConstraints: null, storyArc: null));

        await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);

        Assert.DoesNotContain("Series visual style", f.Provider.LastRequest!.Prompt);
        Assert.DoesNotContain("Rendering style", f.Provider.LastRequest.Prompt);
        Assert.DoesNotContain("noir", f.Provider.LastRequest.Prompt);
        Assert.NotNull(f.Provider.LastRequest.NegativePrompt);
    }

    [Fact]
    public async Task Generate_location_reference_marks_the_location_Generated_and_stores_the_image()
    {
        var f = Build();
        var (story, location) = SeedLocation(f.Repo);

        var result = await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);

        Assert.NotNull(result);
        Assert.Equal(AssetReferenceStatus.Generated.ToString(), result!.Status);
        Assert.Equal(AssetReferenceStatus.Generated, location.ReferenceImageStatus);
        Assert.Contains(location.ReferenceImagePath!, f.Storage.Files.Keys);
        Assert.StartsWith($"stories/{story.Id}/locations/{location.Id}/", location.ReferenceImagePath);
        Assert.False(result.HasPending);
        Assert.Null(result.Source);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Single(f.Usage.Records);
        Assert.Equal("story_location_reference_image_generation", f.Usage.Records[0].Operation);
    }

    [Fact]
    public async Task Approve_location_reference_after_generation_succeeds()
    {
        var f = Build();
        var (story, location) = SeedLocation(f.Repo);
        await f.Service.GenerateLocationReferenceAsync(story.Id, location.Id);

        var result = await f.Service.ApproveLocationReferenceAsync(story.Id, location.Id);

        Assert.NotNull(result);
        Assert.Equal(AssetReferenceStatus.Approved.ToString(), result!.Status);
    }

    // ---- Upload: dimension cap and probe timeout ----

    [Theory]
    [InlineData(9000, 1000)] // longest side over 8192
    [InlineData(1000, 8193)]
    [InlineData(7000, 7000)] // 49 megapixels
    public async Task Upload_rejects_an_image_over_the_dimension_or_pixel_cap_with_a_clear_message_and_stores_nothing(int width, int height)
    {
        var f = Build(probeResult: FakeMediaProbe.GoodInfo(duration: 0.04, w: width, h: height, fps: 25));
        var (story, character) = SeedCharacter(f.Repo);

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png()));

        Assert.Contains("quá lớn", ex.Message);
        Assert.Empty(f.Storage.Files);
        Assert.False(character.HasPendingReferenceImage);
        Assert.Null(character.ReferenceImagePath);
    }

    [Fact]
    public async Task Upload_accepts_an_image_right_at_the_caps()
    {
        var f = Build(probeResult: FakeMediaProbe.GoodInfo(duration: 0.04, w: 8192, h: 4880, fps: 25)); // 39.97 MP
        var (story, character) = SeedCharacter(f.Repo);

        var result = await f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task A_hung_probe_is_cut_off_by_the_timeout_and_reported_generically()
    {
        var f = Build(customProbe: new HangingMediaProbe(), probeTimeout: TimeSpan.FromMilliseconds(50));
        var (story, character) = SeedCharacter(f.Repo);

        var ex = await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png()));

        Assert.Contains("quá thời gian", ex.Message);
        Assert.Empty(f.Storage.Files);
        Assert.NotNull(f.Gate.TryEnter(character.Id)); // the in-flight lease was released
    }

    [Fact]
    public async Task A_caller_cancellation_during_the_probe_still_propagates_as_cancellation()
    {
        var f = Build(customProbe: new HangingMediaProbe());
        var (story, character) = SeedCharacter(f.Repo);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png(), cts.Token));

        Assert.Empty(f.Storage.Files);
    }

    // ---- Stale-entity race: decide on the CURRENT persisted state, not the one loaded before the slow call ----

    private static AiContentFactory.Application.Providers.ImageGenerationResult DefaultImage() =>
        new(new byte[] { 9, 9, 9 }, "image/png", "fake-image");

    [Fact]
    public async Task Scenario_A_an_approve_that_lands_during_generation_is_never_overwritten_by_the_new_image()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        f.Storage.Files["stories/x/old.png"] = new byte[] { 1 };
        character.SetReferenceCandidate("stories/x/old.png", "old prompt", "gemini", ReferenceImageSource.Generated); // Generated (not approved) as loaded
        var databaseRow = f.Repo.SimulateDatabaseRow(character);
        // While the provider call is in flight, another request approves the current image.
        f.Provider.Responder = _ =>
        {
            databaseRow.ApproveReferenceImage();
            return DefaultImage();
        };

        var result = await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        // The new image is only a candidate; the just-approved image and prompt are intact.
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("stories/x/old.png", character.ReferenceImagePath);
        Assert.Equal("old prompt", character.ReferenceImagePrompt);
        Assert.True(character.HasPendingReferenceImage);
        Assert.NotEqual("stories/x/old.png", character.PendingReferenceImagePath);
        Assert.Equal("Approved", result!.Status);
        Assert.True(result.HasPending);
        // ...and that is what got persisted.
        Assert.Equal(AssetReferenceStatus.Approved, databaseRow.ReferenceImageStatus);
        Assert.Equal("stories/x/old.png", databaseRow.ReferenceImagePath);
        Assert.Equal(character.PendingReferenceImagePath, databaseRow.PendingReferenceImagePath);
        Assert.Contains("stories/x/old.png", f.Storage.Files.Keys);
    }

    [Fact]
    public async Task Scenario_A_also_holds_for_an_upload_that_finishes_after_a_concurrent_approve()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        f.Storage.Files["stories/x/old.png"] = new byte[] { 1 };
        character.SetReferenceCandidate("stories/x/old.png", "old prompt", "gemini", ReferenceImageSource.Generated);
        var databaseRow = f.Repo.SimulateDatabaseRow(character);
        databaseRow.ApproveReferenceImage(); // approved by another request before this upload applies its result

        var result = await f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png());

        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Equal("stories/x/old.png", character.ReferenceImagePath);
        Assert.True(result!.HasPending);
        Assert.Equal("Uploaded", result.PendingSource);
    }

    [Fact]
    public async Task Scenario_B_a_pending_image_promoted_during_generation_is_never_deleted()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        f.Storage.Files["stories/x/p1.png"] = new byte[] { 7 };
        character.SetReferenceCandidate("stories/x/p1.png", "p1 prompt", "gemini", ReferenceImageSource.Generated);
        var databaseRow = f.Repo.SimulateDatabaseRow(character);
        // A confirmed approve promotes P1 while this request (which loaded P1 as pending) is still generating.
        f.Provider.Responder = _ =>
        {
            databaseRow.PromotePendingReferenceImage();
            return DefaultImage();
        };

        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);

        Assert.Equal("stories/x/p1.png", character.ReferenceImagePath);
        Assert.Equal(AssetReferenceStatus.Approved, character.ReferenceImageStatus);
        Assert.Contains("stories/x/p1.png", f.Storage.Files.Keys); // the now-approved image is still there
        Assert.True(character.HasPendingReferenceImage);
        Assert.NotEqual("stories/x/p1.png", character.PendingReferenceImagePath);
    }

    [Fact]
    public async Task A_stored_file_that_an_episode_asset_reference_still_points_at_is_never_deleted_when_discarded()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        f.Storage.Files["stories/x/p1.png"] = new byte[] { 7 };
        character.SetReferenceCandidate("stories/x/p1.png", "p1", "gemini", ReferenceImageSource.Generated);
        f.AssetRefs.Rows.Add(AssetReference.CreateGenerated(Guid.NewGuid(), AssetReferenceType.Character, "stories/x/p1.png", "p1", "gemini", label: "Nova"));

        var result = await f.Service.DiscardPendingCharacterReferenceAsync(character.StoryId, character.Id);

        Assert.False(result!.HasPending);
        Assert.Contains("stories/x/p1.png", f.Storage.Files.Keys);
    }

    [Fact]
    public async Task A_replaced_pending_file_that_is_still_referenced_is_kept_by_regeneration_and_upload_but_an_unreferenced_one_is_removed()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        f.Storage.Files["stories/x/shared.png"] = new byte[] { 7 };
        f.Storage.Files["stories/x/lonely.png"] = new byte[] { 8 };
        f.AssetRefs.Rows.Add(AssetReference.CreateGenerated(Guid.NewGuid(), AssetReferenceType.Character, "stories/x/shared.png", "p", "gemini", label: "Nova"));

        character.SetReferenceCandidate("stories/x/shared.png", "p", "gemini", ReferenceImageSource.Generated);
        await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id); // replaces the pending "shared"
        Assert.Contains("stories/x/shared.png", f.Storage.Files.Keys);

        character.SetReferenceCandidate("stories/x/lonely.png", "p", "gemini", ReferenceImageSource.Generated);
        await f.Service.UploadCharacterReferenceAsync(character.StoryId, character.Id, "a.png", Png()); // replaces the pending "lonely"
        Assert.DoesNotContain("stories/x/lonely.png", f.Storage.Files.Keys);
    }

    [Fact]
    public async Task A_file_that_another_story_character_uses_as_its_image_is_never_deleted()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        f.Storage.Files["stories/x/p1.png"] = new byte[] { 7 };
        character.SetReferenceCandidate("stories/x/p1.png", "p1", "gemini", ReferenceImageSource.Generated);
        var other = StoryCharacter.Create(character.StoryId, "Other", null, "v");
        other.SetReferenceCandidate("stories/x/p1.png", "p", "gemini", ReferenceImageSource.Generated);
        f.Repo.Characters[other.Id] = other;

        await f.Service.DiscardPendingCharacterReferenceAsync(character.StoryId, character.Id);

        Assert.Contains("stories/x/p1.png", f.Storage.Files.Keys);
    }

    // ---- In-flight gate ----

    [Fact]
    public async Task A_second_concurrent_generation_for_the_same_character_is_rejected_without_a_second_paid_call()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        Exception? inner = null;
        f.Provider.Responder = _ =>
        {
            try
            {
                f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                inner = ex;
            }

            return DefaultImage();
        };

        var result = await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id);

        Assert.NotNull(result);
        Assert.IsType<ReferenceOperationInProgressException>(inner);
        Assert.IsAssignableFrom<DomainException>(inner);
        Assert.Contains("đang có một thao tác", inner!.Message);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Single(f.Usage.Records);
    }

    [Fact]
    public async Task The_gate_is_released_after_a_provider_failure_after_success_and_after_a_rejected_upload()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        f.Provider.Throw = new HttpRequestException("500");
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));
        f.Provider.Throw = null;

        Assert.NotNull(await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id)); // not stuck after the failure
        Assert.NotNull(await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id)); // not stuck after success

        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.gif", Png()));
        await Assert.ThrowsAsync<DomainException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", new MemoryStream(new byte[] { 1, 2 })));
        Assert.NotNull(await f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png())); // not stuck after rejections
    }

    [Fact]
    public async Task While_an_operation_is_in_flight_both_generate_and_upload_are_rejected_and_nothing_is_spent_or_stored()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);

        using (f.Gate.TryEnter(character.Id))
        {
            await Assert.ThrowsAsync<ReferenceOperationInProgressException>(() => f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));
            await Assert.ThrowsAsync<ReferenceOperationInProgressException>(() => f.Service.UploadCharacterReferenceAsync(story.Id, character.Id, "a.png", Png()));
            Assert.Equal(0, f.Provider.Calls);
            Assert.Empty(f.Storage.Files);
            Assert.Empty(f.Usage.Records);
        }

        Assert.NotNull(await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id));
    }

    [Fact]
    public void The_gate_is_per_character_and_its_lease_can_be_disposed_repeatedly()
    {
        var gate = new StoryReferenceGenerationGate();
        var a = Guid.NewGuid();

        var lease = gate.TryEnter(a);
        Assert.NotNull(lease);
        Assert.Null(gate.TryEnter(a));
        using (var other = gate.TryEnter(Guid.NewGuid()))
        {
            Assert.NotNull(other);
        }

        lease!.Dispose();
        lease.Dispose(); // idempotent - must not release somebody else's later lease
        var second = gate.TryEnter(a);
        Assert.NotNull(second);
        lease.Dispose();
        Assert.Null(gate.TryEnter(a));
        second!.Dispose();
    }

    // ---- pendingVersion: an approve is tied to the candidate the user actually saw ----

    [Fact]
    public void PendingVersion_is_a_stable_12_char_hex_of_the_pending_path_and_null_without_one()
    {
        var a = ReferencePendingVersion.Of("stories/x/p1.png");

        Assert.Matches("^[0-9a-f]{12}$", a);
        Assert.Equal(a, ReferencePendingVersion.Of("stories/x/p1.png"));
        Assert.NotEqual(a, ReferencePendingVersion.Of("stories/x/p2.png"));
        Assert.Null(ReferencePendingVersion.Of((string?)null));
        Assert.Null(ReferencePendingVersion.Of("  "));
    }

    [Fact]
    public async Task Responses_carry_the_pending_version_only_while_a_candidate_exists()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        Assert.Null(StoryCharacterResponse.FromDomain(character).PendingVersion);

        var generated = await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);

        var expected = ReferencePendingVersion.Of(character.PendingReferenceImagePath);
        Assert.NotNull(expected);
        Assert.Equal(expected, generated!.PendingVersion);
        Assert.Equal(expected, StoryCharacterResponse.FromDomain(character).PendingVersion);

        var discarded = await f.Service.DiscardPendingCharacterReferenceAsync(character.StoryId, character.Id);
        Assert.Null(discarded!.PendingVersion);
    }

    [Fact]
    public async Task Approving_with_a_matching_expected_version_promotes_the_candidate()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        var generated = await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);
        var pendingPath = character.PendingReferenceImagePath;

        var result = await f.Service.ApproveCharacterReferenceAsync(character.StoryId, character.Id, confirmReplace: true, expectedPendingVersion: generated!.PendingVersion);

        Assert.False(result!.HasPending);
        Assert.Equal(pendingPath, character.ReferenceImagePath);
    }

    [Fact]
    public async Task A_confirm_shown_for_candidate_A_cannot_promote_candidate_B_that_arrived_from_another_tab()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        var candidateA = await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);
        // Another tab uploads candidate B, replacing A as the pending image.
        await f.Service.UploadCharacterReferenceAsync(character.StoryId, character.Id, "b.png", Png());
        var pendingB = character.PendingReferenceImagePath;

        var ex = await Assert.ThrowsAsync<ReferencePendingChangedException>(() =>
            f.Service.ApproveCharacterReferenceAsync(character.StoryId, character.Id, confirmReplace: true, expectedPendingVersion: candidateA!.PendingVersion));

        Assert.IsAssignableFrom<DomainException>(ex);
        Assert.Equal("stories/x/approved.png", character.ReferenceImagePath);
        Assert.Equal(pendingB, character.PendingReferenceImagePath); // nothing was promoted or dropped
    }

    [Fact]
    public async Task An_expected_version_with_no_pending_candidate_at_all_is_a_mismatch_and_an_omitted_one_keeps_todays_behaviour()
    {
        var f = Build();
        var (story, character) = SeedCharacter(f.Repo);
        await f.Service.GenerateCharacterReferenceAsync(story.Id, character.Id); // Generated, no pending

        await Assert.ThrowsAsync<ReferencePendingChangedException>(() =>
            f.Service.ApproveCharacterReferenceAsync(story.Id, character.Id, expectedPendingVersion: "abcdef012345"));
        Assert.Equal(AssetReferenceStatus.Generated, character.ReferenceImageStatus);

        var legacy = await f.Service.ApproveCharacterReferenceAsync(story.Id, character.Id); // version omitted
        Assert.Equal("Approved", legacy!.Status);
    }

    [Fact]
    public async Task The_replace_confirmation_is_still_required_even_with_a_matching_version()
    {
        var f = Build();
        var character = await ApprovedCharacterAsync(f);
        var generated = await f.Service.GenerateCharacterReferenceAsync(character.StoryId, character.Id);

        await Assert.ThrowsAsync<ReferenceReplaceConfirmationRequiredException>(() =>
            f.Service.ApproveCharacterReferenceAsync(character.StoryId, character.Id, confirmReplace: false, expectedPendingVersion: generated!.PendingVersion));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
