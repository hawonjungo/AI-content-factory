using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Storyboards;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

public class FlowPlanServiceTests
{
    private readonly Guid _project = Guid.NewGuid();

    private static Storyboard StoryboardWith(Guid project)
    {
        var sb = Storyboard.Create(project);
        var hook = sb.AddScene(8, "The stray cat walked into the office.", "wide establishing shot", "slow push in", SceneVisualType.AiVideo);
        hook.SetAllocation(100, "Fast", "Hero clip: highest AI-video priority.");
        var beat1 = sb.AddScene(8, "It joined the standup rotation.", "cat on a chair", "handheld", SceneVisualType.AiVideo);
        beat1.SetAllocation(70, "Lite", "Story-beat clip.");
        var beat2 = sb.AddScene(8, "Someone explained a bug to it.", "engineer talking", "static", SceneVisualType.AiVideo);
        beat2.SetAllocation(55, "Lite", "Story-beat clip.");
        var still = sb.AddScene(8, "Follow for more.", "closing card", "none", SceneVisualType.AiImage);
        still.SetAllocation(20, null, "CTA: still image.");
        return sb;
    }

    private (FlowGenerationPlanService Service, CreditLedgerService Ledger, FakeAssetReferenceRepository Refs, FakeStoryVisualContextResolver StoryVisualContext)
        Build(Storyboard? storyboard = null)
    {
        var project = ContentProject.Create("Office Cat", "topic", "storytelling", 60, "9:16", "en");
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        var refs = new FakeAssetReferenceRepository();
        var storyVisualContext = new FakeStoryVisualContextResolver();

        var service = new FlowGenerationPlanService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardRepository(storyboard ?? StoryboardWith(_project)),
            refs,
            ledger,
            storyVisualContext,
            Options.Create(new CreditCostOptions()),
            Options.Create(new FlowModelOptions()));

        return (service, ledger, refs, storyVisualContext);
    }

    [Fact]
    public async Task Plan_lists_only_the_scenes_that_need_flow_and_prices_them()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);

        Assert.Equal(3, plan.ScenesRequiringFlow);                     // 3 AI_VIDEO, 1 AI_IMAGE
        Assert.Equal(20 + 10 + 10, plan.PlannedCredits);              // Fast + Lite + Lite
        Assert.Equal(50, plan.DailyBudgetCredits);
        Assert.True(plan.WithinBudget);

        var video = plan.Scenes.Where(s => s.GenerationType == "AI_VIDEO").ToList();
        Assert.Equal("Fast", video[0].RecommendedModel);
        Assert.All(video, s => Assert.False(string.IsNullOrWhiteSpace(s.FlowVideoPrompt)));
        Assert.All(video, s => Assert.True(s.EstimatedCredits > 0));
    }

    [Fact]
    public async Task Plan_does_not_force_spending_the_whole_daily_budget()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);

        Assert.True(plan.PlannedCredits < plan.DailyBudgetCredits); // 40 < 50
    }

    [Fact]
    public async Task An_ai_image_scene_carries_no_flow_model_and_is_not_counted_as_flow_work()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);
        var stillScene = Assert.Single(plan.Scenes, s => s.GenerationType == "AI_IMAGE");

        Assert.Null(stillScene.RecommendedModel);
        Assert.DoesNotContain(plan.Scenes.Where(s => s.GenerationType == "AI_VIDEO"), s => s.SceneNumber == stillScene.SceneNumber);
    }

    [Fact]
    public async Task Plan_flags_when_it_would_not_fit_the_remaining_flow_credits()
    {
        var (service, ledger, _, _) = Build();

        // Book 45 credits already spent today -> only 5 remain, plan needs 40.
        await ledger.RecordExternalCompletionAsync(
            new CreditReservationRequest(_project, Guid.NewGuid(), GenerationKind.Video, "google-flow", "google-flow", VideoModelTier.Fast), 45);

        var plan = await service.BuildAsync(_project);

        Assert.Equal(45, plan.UsedCredits);
        Assert.Equal(5, plan.RemainingCredits);
        Assert.Equal(40, plan.PlannedCredits);
        Assert.False(plan.WithinBudget);
    }

    [Fact]
    public async Task Approved_reference_images_are_attached_to_character_scenes()
    {
        var (service, _, refs, _) = Build();
        var character = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/char.png", null, "upload");
        character.Approve();
        refs.Rows.Add(character);

        var plan = await service.BuildAsync(_project);
        var heroScene = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.True(heroScene.CharacterRequired);
        Assert.Contains(heroScene.ReferenceImageUrls, u => u.Contains(character.Id.ToString()) && u.EndsWith("/file"));
    }

    [Fact]
    public async Task CharacterReferenceImageUrl_is_offered_even_on_a_scene_the_allocator_did_not_flag_as_needing_it()
    {
        var (service, _, refs, _) = Build();
        var character = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/char.png", null, "upload");
        character.Approve();
        refs.Rows.Add(character);

        var plan = await service.BuildAsync(_project);
        var ctaStill = plan.Scenes.First(s => s.GenerationType == "AI_IMAGE"); // CharacterRequired=false (priority 20, AiImage)

        Assert.False(ctaStill.CharacterRequired);
        Assert.NotNull(ctaStill.CharacterReferenceImageUrl);
    }

    [Fact]
    public async Task RecommendFreeTool_is_true_for_every_ai_video_scene_when_there_is_no_character_reference()
    {
        var (service, _, _, _) = Build(); // no approved Character reference

        var plan = await service.BuildAsync(_project);

        Assert.All(plan.Scenes.Where(s => s.GenerationType == "AI_VIDEO"), s => Assert.True(s.RecommendFreeTool));
    }

    [Fact]
    public async Task RecommendFreeTool_reflects_whether_the_scenes_own_action_puts_a_person_on_screen()
    {
        var sb = Storyboard.Create(_project);
        var establishing = sb.AddScene(8, "narration", "Aerial drone shot gliding over the city skyline at sunset", "slow push in", SceneVisualType.AiVideo);
        establishing.SetAllocation(100, "Fast", "Hero clip.");
        var withPerson = sb.AddScene(8, "narration", "She smiles and picks up the phone, laughing softly", "static", SceneVisualType.AiVideo);
        withPerson.SetAllocation(70, "Lite", "Story-beat clip.");

        var (service, _, refs, _) = Build(sb);
        var character = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/char.png", null, "upload");
        character.Approve();
        refs.Rows.Add(character);

        var plan = await service.BuildAsync(_project);

        Assert.True(plan.Scenes.Single(s => s.SceneNumber == 1).RecommendFreeTool);
        Assert.False(plan.Scenes.Single(s => s.SceneNumber == 2).RecommendFreeTool);
    }

    [Fact]
    public async Task CopyAll_text_bundles_every_flow_prompt_in_the_english_only_export_format()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);

        Assert.Contains("--- SCENE 1 (Veo Fast - 20 credits) ---", plan.CopyAllText);
        Assert.Contains("--- SCENE 2 (Veo Lite - 10 credits) ---", plan.CopyAllText);
        Assert.Contains("--- SCENE 3 (Veo Lite - 10 credits) ---", plan.CopyAllText);
        Assert.DoesNotContain("SCENE 4", plan.CopyAllText); // the AI_IMAGE scene

        // No Vietnamese instructional text, no fenced "paste from here" markers.
        foreach (var banned in new[] { "RIÊNG BIỆT", "dán từ đây", "đến đây", "Cảnh ", "clip khác nhau", "###", "----" })
        {
            Assert.DoesNotContain(banned, plan.CopyAllText);
        }
    }

    [Fact]
    public async Task CopyAll_text_leaves_out_clips_the_user_already_has_a_video_for()
    {
        var storyboard = StoryboardWith(_project);
        storyboard.Scenes.First().SetSkipGeneration(true); // scene 1 (the Fast hero) already imported

        var (service, _, _, _) = Build(storyboard);

        var plan = await service.BuildAsync(_project);

        Assert.DoesNotContain("SCENE 1", plan.CopyAllText);
        Assert.Contains("--- SCENE 2 (Veo Lite - 10 credits) ---", plan.CopyAllText);
        Assert.Equal(10 + 10, plan.PlannedCredits);   // the two Lite beats only
        Assert.Equal(2, plan.ScenesRequiringFlow);
    }

    [Fact]
    public async Task CopyAll_text_excludes_unprompted_scenes_and_reports_how_many_were_skipped()
    {
        var sb = Storyboard.Create(_project);
        var prompted = sb.AddScene(8, "The stray cat walked into the office.", "wide establishing shot", "slow push in", SceneVisualType.AiVideo);
        prompted.SetAllocation(100, "Fast", "Hero clip.");
        // Blank VisualDescription/CameraDirection, no GenerationPrompt - the
        // exact pre-"Gợi ý prompt" state ClipPlanService.GenerateAsync leaves scenes in.
        var unprompted = sb.AddScene(8, "It joined the standup rotation.", string.Empty, string.Empty, SceneVisualType.AiVideo);
        unprompted.SetAllocation(70, "Lite", "Story-beat clip.");

        var (service, _, _, _) = Build(sb);

        var plan = await service.BuildAsync(_project);

        Assert.True(plan.Scenes.Single(s => s.SceneNumber == 2).IsUnprompted);
        Assert.Contains("--- SCENE 1 (Veo Fast - 20 credits) ---", plan.CopyAllText);
        Assert.DoesNotContain("SCENE 2", plan.CopyAllText);
        // No raw narration ("It joined the standup rotation.") leaking into the batch export.
        Assert.DoesNotContain("standup rotation", plan.CopyAllText);
        // A clear, English summary that a scene was left out - not a silently smaller batch.
        Assert.Contains("1 scene(s) skipped - not yet prompted", plan.CopyAllText);
    }

    [Fact]
    public async Task Flow_video_prompt_is_the_flowing_paragraph_export_format()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");
        var prompt = hero.FlowVideoPrompt;

        // The Flow export is one flowing cinematic paragraph (replacing the
        // earlier bracketed block format) - a human pastes this manually.
        foreach (var marker in new[] { "[VISUAL ACTION]", "[CAMERA & STYLE]", "[LOCATION]", "[CHARACTER REF]", "[NOTE]", "Action:", "Camera:", "Style:", "Format:", "Restrictions:", "Camera / motion:", "---", "###" })
        {
            Assert.DoesNotContain(marker, prompt);
        }
        Assert.Matches(@"^[A-Z].*\.$", prompt);

        // One deterministic camera move; ends with the vertical format + restriction.
        Assert.Contains("Slow subtle push-in", prompt);
        Assert.DoesNotContain("push-in or handheld drift", prompt);
        Assert.EndsWith("No text, subtitles, logos, or watermarks.", prompt);

        // Deterministic: same storyboard -> byte-identical prompt.
        var again = await service.BuildAsync(_project);
        Assert.Equal(prompt, again.Scenes.First(s => s.GenerationType == "AI_VIDEO").FlowVideoPrompt);
    }

    [Fact]
    public async Task Unnamed_legacy_anchors_do_not_add_named_character_or_location_content()
    {
        // A non-Story project's classic single Character/Environment anchors
        // have no real name (Label is null) and the scene has no tags, so the
        // per-scene name match finds nothing - the Flow export correctly has
        // no named-character/location content to show, even though reference
        // image URLs are still offered separately (see ReferenceImageUrls tests).
        var (service, _, refs, _) = Build();
        foreach (var type in new[] { AssetReferenceType.Character, AssetReferenceType.Environment })
        {
            var r = AssetReference.CreateGenerated(_project, type, $"path/{type}.png", null, "upload");
            r.Approve();
            refs.Rows.Add(r);
        }

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.DoesNotContain("refer to the attached", hero.FlowVideoPrompt);
        Assert.Contains(hero.ReferenceImageUrls, u => !string.IsNullOrWhiteSpace(u));
        // No tagged/matched names -> the new per-scene list is empty too (the
        // legacy singular CharacterReferenceImageUrl is what still carries
        // this classic single unnamed anchor - unaffected, see below).
        Assert.Empty(hero.CharacterReferenceImages);
        Assert.NotNull(hero.CharacterReferenceImageUrl);
    }

    [Fact]
    public async Task Tagged_scene_with_named_approved_references_shows_character_ref_and_location_lines()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo climbed the mast toward Ha Long Bay.", "cat climbing a mast", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Ha Long Bay" });

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var haLongBay = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/halongbay.png", null, "upload", label: "Ha Long Bay");
        haLongBay.Approve();
        refs.Rows.Add(haLongBay);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Contains("Milo", hero.FlowVideoPrompt);
        Assert.Contains("Ha Long Bay", hero.FlowVideoPrompt);
        Assert.Contains("(refer to the attached Milo reference)", hero.FlowVideoPrompt);
    }

    [Fact]
    public async Task A_scene_tagged_with_two_characters_exposes_both_of_their_images_not_just_one()
    {
        // The bug this fixes: CharacterReferenceImageUrl (legacy, singular)
        // only ever exposes the project's first-approved Character anchor, so
        // a scene naming both Milo AND Mimi had no way to offer Mimi's image
        // at all. CharacterReferenceImages must carry both, scoped to this
        // scene's own tagged/matched set.
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo and Mimi chased the yarn together.", "two cats playing", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Mimi" });

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var mimi = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/mimi.png", null, "upload", label: "Mimi");
        mimi.Approve();
        refs.Rows.Add(mimi);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Equal(2, hero.CharacterReferenceImages.Count);
        Assert.Contains(hero.CharacterReferenceImages, i => i.Name == "Milo" && i.Url.Contains(milo.Id.ToString()) && i.Url.EndsWith("/file"));
        Assert.Contains(hero.CharacterReferenceImages, i => i.Name == "Mimi" && i.Url.Contains(mimi.Id.ToString()) && i.Url.EndsWith("/file"));
        // Never the same URL twice, and never the legacy single-slot value standing in for both.
        Assert.NotEqual(
            hero.CharacterReferenceImages[0].Url,
            hero.CharacterReferenceImages[1].Url);
    }

    [Fact]
    public async Task A_scene_tagged_with_one_character_exposes_exactly_one_reference_image()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo climbed the mast.", "cat climbing", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Milo" });

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        var image = Assert.Single(hero.CharacterReferenceImages);
        Assert.Equal("Milo", image.Name);
        Assert.Contains(milo.Id.ToString(), image.Url);
    }

    [Fact]
    public async Task A_scene_with_no_tagged_or_matched_character_has_an_empty_character_reference_images_list_not_a_crash()
    {
        var (service, _, _, _) = Build(); // StoryboardWith() scenes have no RelevantReferenceLabels

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Empty(hero.CharacterReferenceImages);
    }

    [Fact]
    public async Task CharacterReferenceImageUrl_legacy_singular_field_is_unaffected_by_the_new_list_field()
    {
        // Regression: the legacy single-slot field must still return exactly
        // what it did before, even on a scene that now also gets a
        // multi-entry CharacterReferenceImages list.
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo and Mimi chased the yarn together.", "two cats playing", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Mimi" });

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var mimi = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/mimi.png", null, "upload", label: "Mimi");
        mimi.Approve();
        refs.Rows.Add(mimi);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        // Legacy field: unconditional (CharacterRequired doesn't gate it),
        // resolved from the project-level UrlFor(Character) first-approved
        // row (Milo, added first) - unchanged behavior.
        Assert.NotNull(hero.CharacterReferenceImageUrl);
        Assert.Contains(milo.Id.ToString(), hero.CharacterReferenceImageUrl);
        // The new list is unaffected in the other direction too - both entries present.
        Assert.Equal(2, hero.CharacterReferenceImages.Count);
    }

    [Fact]
    public async Task Tagged_scene_with_an_approved_location_gets_a_scene_specific_environment_reference_url()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo climbed the mast toward Ha Long Bay.", "cat climbing a mast", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Ha Long Bay" });

        var (service, _, refs, _) = Build(sb);
        var haLongBay = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/halongbay.png", null, "upload", label: "Ha Long Bay");
        haLongBay.Approve();
        refs.Rows.Add(haLongBay);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.NotNull(hero.EnvironmentReferenceImageUrl);
        Assert.Contains(haLongBay.Id.ToString(), hero.EnvironmentReferenceImageUrl);
        Assert.EndsWith("/file", hero.EnvironmentReferenceImageUrl);
    }

    [Fact]
    public async Task Scene_with_no_location_tag_or_no_matching_approved_reference_has_a_null_environment_reference_url()
    {
        var (service, _, refs, _) = Build(); // StoryboardWith() scenes have no RelevantReferenceLabels
        var haLongBay = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/halongbay.png", null, "upload", label: "Ha Long Bay");
        haLongBay.Approve();
        refs.Rows.Add(haLongBay);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Null(hero.EnvironmentReferenceImageUrl);
    }

    [Fact]
    public async Task Environment_reference_url_reflects_the_scenes_own_matched_location_not_the_legacy_single_slot_anchor()
    {
        // Two approved locations: a legacy (unnamed) Environment anchor that
        // would populate the old project-level environmentUrl, and a named
        // one this scene is actually tagged for. The new field must point at
        // the scene's tagged location (Da Nang), never the unrelated legacy
        // slot value.
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "The drone glided over Da Nang.", "aerial shot", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Da Nang" });

        var (service, _, refs, _) = Build(sb);
        var legacyAnchor = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/legacy.png", null, "upload");
        legacyAnchor.Approve();
        refs.Rows.Add(legacyAnchor);
        var daNang = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/danang.png", null, "upload", label: "Da Nang");
        daNang.Approve();
        refs.Rows.Add(daNang);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.NotNull(hero.EnvironmentReferenceImageUrl);
        Assert.Contains(daNang.Id.ToString(), hero.EnvironmentReferenceImageUrl);
        Assert.DoesNotContain(legacyAnchor.Id.ToString(), hero.EnvironmentReferenceImageUrl);
    }

    [Fact]
    public async Task An_empty_storyboard_yields_an_empty_plan()
    {
        var (service, _, _, _) = Build(Storyboard.Create(_project));

        var plan = await service.BuildAsync(_project);

        Assert.Empty(plan.Scenes);
        Assert.Equal(0, plan.PlannedCredits);
        Assert.Equal(0, plan.ScenesRequiringFlow);
    }

    [Fact]
    public async Task Known_visual_descriptions_enrich_character_ref_and_location_lines()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo climbed the mast toward Ha Long Bay.", "cat climbing a mast", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Ha Long Bay" });

        var (service, _, refs, storyVisualContext) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var haLongBay = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/halongbay.png", null, "upload", label: "Ha Long Bay");
        haLongBay.Approve();
        refs.Rows.Add(haLongBay);
        storyVisualContext.CastAndLocationVisualDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Milo"] = "orange tabby, one white paw, green eyes",
            ["Ha Long Bay"] = "limestone karsts, calm emerald water, junk boats",
        };

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        // The description is woven in as a short appositive trait clause with
        // a short inline reference tag - not a verbose "use the attached
        // reference image for exact likeness" instruction sentence.
        Assert.Contains("Milo, orange tabby, one white paw, green eyes (refer to the attached Milo reference)", hero.FlowVideoPrompt);
        Assert.Contains("Ha Long Bay, limestone karsts, calm emerald water, junk boats", hero.FlowVideoPrompt);
        Assert.DoesNotContain("use the attached reference image for exact likeness", hero.FlowVideoPrompt);
        Assert.DoesNotContain("[NOTE]", hero.FlowVideoPrompt);
    }

    [Fact]
    public async Task Unknown_visual_description_falls_back_to_bare_name_with_no_empty_parens()
    {
        // Same tagged scene as above, but the resolver knows this is a
        // Story-linked project (non-null dictionary) without an entry for
        // either name - must fall back to the bare name, never print "()".
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo climbed the mast toward Ha Long Bay.", "cat climbing a mast", "slow push in", SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Ha Long Bay" });

        var (service, _, refs, storyVisualContext) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var haLongBay = AssetReference.CreateGenerated(_project, AssetReferenceType.Environment, "path/halongbay.png", null, "upload", label: "Ha Long Bay");
        haLongBay.Approve();
        refs.Rows.Add(haLongBay);
        storyVisualContext.CastAndLocationVisualDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Someone Else"] = "unrelated character",
        };

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Contains("Milo (refer to the attached Milo reference)", hero.FlowVideoPrompt);
        Assert.Contains("Ha Long Bay", hero.FlowVideoPrompt);
        Assert.DoesNotContain("()", hero.FlowVideoPrompt);
    }

    [Fact]
    public async Task A_scene_with_a_real_ai_prompt_is_not_flagged_as_unprompted()
    {
        var (service, _, _, _) = Build(); // StoryboardWith() gives every scene a VisualDescription

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.False(hero.IsUnprompted);
        Assert.DoesNotContain("⚠️", hero.FlowVideoPrompt);
        Assert.DoesNotContain("Chưa qua AI", hero.FlowVideoPrompt);
        Assert.False(string.IsNullOrWhiteSpace(hero.FlowVideoPrompt));
    }

    [Fact]
    public async Task A_scene_with_no_ai_prompt_or_visual_description_is_flagged_and_never_leaks_raw_narration()
    {
        var sb = Storyboard.Create(_project);
        // Mirrors what ClipPlanService.GenerateAsync actually creates: blank
        // VisualDescription/CameraDirection, no GenerationPrompt/CameraMovement
        // set - i.e. before the user ever runs "Gợi ý prompt" for this scene.
        var scene = sb.AddScene(8, "The cat knocked a mug off the desk.", string.Empty, string.Empty, SceneVisualType.AiVideo);
        scene.SetAllocation(100, "Fast", "Hero clip.");

        var (service, _, _, _) = Build(sb);

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.Single();

        // IsUnprompted is the authoritative signal for the frontend's own
        // Vietnamese warning banner. FlowVideoPrompt itself must never carry
        // the raw Narration/dialogue text (a real bug this fixes: it used to
        // silently fall back to voiceover text dressed up as a visual
        // description) - it is left empty instead, so there is nothing unsafe
        // to accidentally paste into Flow.
        Assert.True(hero.IsUnprompted);
        Assert.Equal(string.Empty, hero.FlowVideoPrompt);
        Assert.DoesNotContain("cat knocked a mug", hero.FlowVideoPrompt);
    }

    [Fact]
    public async Task Non_story_project_with_a_real_prompt_is_unaffected_by_the_new_enrichment()
    {
        // A normal (non-Story) project: the resolver returns null for the
        // description map (the default/every-non-Story-project case) - the
        // export must be byte-identical in shape to before this feature,
        // no crash, no empty-paren artifact.
        var (service, _, _, storyVisualContext) = Build();
        storyVisualContext.CastAndLocationVisualDescriptions = null;

        var plan = await service.BuildAsync(_project);
        var hero = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.False(hero.IsUnprompted);
        Assert.DoesNotContain("()", hero.FlowVideoPrompt);
        Assert.DoesNotContain("⚠️", hero.FlowVideoPrompt);
    }

    // --- ImagePrompt (ComposeImagePrompt) -----------------------------------
    //
    // Bug fixed: 1) raw Narration/dialogue leaking into ImagePrompt when
    // VisualDescription/GenerationPrompt were blank; 2) a hardcoded
    // "real human, correct anatomy" instruction unconditionally appended
    // whenever a Character reference existed, regardless of the actual
    // character's species. Both are the AI_IMAGE-scoped counterpart of the
    // fix already applied to FlowVideoPrompt/ComposeFlowPrompt above.

    /// <summary>Category 1 + 6: two known (non-human) characters with resolved visual descriptions.</summary>
    [Fact]
    public async Task Image_prompt_names_known_characters_with_their_real_visual_descriptions_no_anatomy_language()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo and Mimi posed for the closing card.", "Milo and Mimi sitting side by side on the windowsill", "none", SceneVisualType.AiImage);
        scene.SetAllocation(20, null, "CTA: still image.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Mimi" });

        var (service, _, refs, storyVisualContext) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var mimi = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/mimi.png", null, "upload", label: "Mimi");
        mimi.Approve();
        refs.Rows.Add(mimi);
        storyVisualContext.CastAndLocationVisualDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Milo"] = "a small orange tabby cat with round green eyes",
            ["Mimi"] = "a sleek grey cat with a white chest patch",
        };

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");

        Assert.Contains("Milo (a small orange tabby cat with round green eyes)", still.ImagePrompt);
        Assert.Contains("Mimi (a sleek grey cat with a white chest patch)", still.ImagePrompt);
        Assert.DoesNotContain("real human", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct anatomy", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[", still.ImagePrompt);
        Assert.DoesNotContain("]", still.ImagePrompt);
    }

    /// <summary>Category 2: narration/dialogue exists but no GenerationPrompt/VisualDescription -> ImagePrompt is empty, never the dialogue text.</summary>
    [Fact]
    public async Task Image_prompt_never_leaks_raw_narration_when_unprompted()
    {
        var sb = Storyboard.Create(_project);
        // Mirrors ClipPlanService.GenerateAsync's freshly-split state: blank
        // VisualDescription/CameraDirection, no GenerationPrompt.
        var scene = sb.AddScene(8, "While I, Mimi, was reviewing the budget spreadsheet, Milo knocked my coffee over.", string.Empty, string.Empty, SceneVisualType.AiImage);
        scene.SetAllocation(20, null, "CTA: still image.");

        var (service, _, _, _) = Build(sb);

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single();

        Assert.True(still.IsUnprompted);
        Assert.Equal(string.Empty, still.ImagePrompt);
        Assert.DoesNotContain("Mimi, was reviewing", still.ImagePrompt);
        Assert.DoesNotContain("budget spreadsheet", still.ImagePrompt);
    }

    /// <summary>Category 3: two character assets tagged on the same image scene -> both represented.</summary>
    [Fact]
    public async Task Image_prompt_represents_both_characters_tagged_on_the_scene()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo and Mimi chased the yarn together.", "two cats playing on the rug", "none", SceneVisualType.AiImage);
        scene.SetAllocation(20, null, "CTA: still image.");
        scene.SetRelevantReferenceLabels(new[] { "Milo", "Mimi" });

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var mimi = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/mimi.png", null, "upload", label: "Mimi");
        mimi.Approve();
        refs.Rows.Add(mimi);

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");

        Assert.Contains("Milo", still.ImagePrompt);
        Assert.Contains("Mimi", still.ImagePrompt);
    }

    /// <summary>Category 4: a Character reference exists but no name/description is known (legacy/non-Story project) -> graceful, species-neutral fallback, no crash, no null/"undefined" artifacts.</summary>
    [Fact]
    public async Task Image_prompt_falls_back_to_a_species_neutral_instruction_when_no_named_character_is_known()
    {
        var sb = Storyboard.Create(_project);
        // Priority >= 40 (and no RelevantReferenceLabels/named match) so
        // CharacterRequired/hasCharacter is true (see SceneResponse.FromDomain)
        // while the per-scene name match still finds nothing - the classic
        // single unnamed Character anchor case.
        var scene = sb.AddScene(8, "The pet posed for the closing card.", "the pet sitting on the windowsill", "none", SceneVisualType.AiImage);
        scene.SetAllocation(50, null, "CTA: still image, featured.");

        var (service, _, refs, _) = Build(sb);
        var character = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/char.png", null, "upload");
        character.Approve();
        refs.Rows.Add(character);

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");
        Assert.True(still.CharacterRequired); // sanity: exercising the hasCharacter=true, no-name branch

        Assert.Contains("Feature the main character exactly as shown in the attached reference image.", still.ImagePrompt);
        Assert.DoesNotContain("real human", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correct anatomy", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("null", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undefined", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Category 4b: no Character reference relevant to the scene at all -> no character clause, no crash.</summary>
    [Fact]
    public async Task Image_prompt_has_no_character_clause_when_no_character_reference_exists()
    {
        var (service, _, _, _) = Build(); // no approved Character reference at all

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");

        Assert.DoesNotContain("Feature", still.ImagePrompt);
        Assert.DoesNotContain("real human", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Category 5: the fix removes the wrong hardcoded species assumption, it
    /// does not special-case "reject human" - a real human character's
    /// VisualDescription is carried through exactly like any other.
    /// </summary>
    [Fact]
    public async Task Image_prompt_works_fine_for_a_genuinely_human_character_when_the_data_says_so()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "The presenter introduced the topic.", "the presenter standing at a desk", "none", SceneVisualType.AiImage);
        scene.SetAllocation(20, null, "CTA: still image.");
        scene.SetRelevantReferenceLabels(new[] { "Alex" });

        var (service, _, refs, storyVisualContext) = Build(sb);
        var alex = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/alex.png", null, "upload", label: "Alex");
        alex.Approve();
        refs.Rows.Add(alex);
        storyVisualContext.CastAndLocationVisualDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alex"] = "a woman in her 30s with short black hair, wearing a blue blazer",
        };

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");

        Assert.Contains("Alex (a woman in her 30s with short black hair, wearing a blue blazer)", still.ImagePrompt);
    }

    /// <summary>Category 7 + 9: the old "Photorealistic cinematic ... still:" wording is gone; the still-image framing itself is kept (this method is confirmed image-only), just as a single coherent sentence-based paragraph with no brackets/labels.</summary>
    [Fact]
    public async Task Image_prompt_uses_the_cleaned_up_photograph_opening_with_no_brackets_or_labels()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);
        var still = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");

        Assert.StartsWith("A photorealistic vertical 9:16 photograph:", still.ImagePrompt);
        // The old contradictory "cinematic ... still" opening is gone. The
        // project's own style guidance text may still legitimately contain
        // "cinematic" as a style descriptor (e.g. "cinematic film still,
        // dramatic low-key lighting..." - the default "cinematic-dark"
        // preset) - that is unrelated project-level content, not the
        // structural opening phrase this fix addresses.
        Assert.DoesNotContain("Photorealistic cinematic", still.ImagePrompt);
        Assert.DoesNotContain("cinematic vertical 9:16 still", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        foreach (var marker in new[] { "[VISUAL", "[CAMERA", "[LOCATION", "[CHARACTER", "[NOTE", "Action:", "Camera:", "Style:", "Format:", "Restrictions:", "---", "###" })
        {
            Assert.DoesNotContain(marker, still.ImagePrompt);
        }
    }

    /// <summary>Category 8: the exact old phrase must never appear anywhere in the output, for any input shape.</summary>
    [Fact]
    public async Task Image_prompt_never_contains_the_old_hardcoded_human_anatomy_phrase()
    {
        var sb = Storyboard.Create(_project);
        var namedScene = sb.AddScene(8, "Milo posed for the card.", "Milo sitting on the windowsill", "none", SceneVisualType.AiImage);
        namedScene.SetAllocation(20, null, "CTA: still image.");
        namedScene.SetRelevantReferenceLabels(new[] { "Milo" });
        var genericScene = sb.AddScene(8, "The pet posed for the card.", "the pet sitting on the windowsill", "none", SceneVisualType.AiImage);
        genericScene.SetAllocation(15, null, "CTA: still image.");

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);

        var plan = await service.BuildAsync(_project);

        Assert.All(plan.Scenes, s =>
        {
            Assert.DoesNotContain("real human", s.ImagePrompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("correct anatomy", s.ImagePrompt, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Category 10: the direct-provider AI_IMAGE path (ImageGenerationService)
    /// and the video paths are untouched by this fix - only FlowGenerationPlanService's
    /// ImagePrompt composition changed. Regression check: the video prompt
    /// tests above (Flow_video_prompt_is_the_flowing_paragraph_export_format,
    /// Known_visual_descriptions_enrich_character_ref_and_location_lines, etc.)
    /// still pass unchanged, and ImagePrompt/FlowVideoPrompt remain separate,
    /// independently-composed fields.
    /// </summary>
    [Fact]
    public async Task Image_prompt_and_video_prompt_remain_independent_fields()
    {
        var sb = Storyboard.Create(_project);
        var video = sb.AddScene(8, "Milo climbed the mast.", "cat climbing a mast", "slow push in", SceneVisualType.AiVideo);
        video.SetAllocation(100, "Fast", "Hero clip.");
        video.SetRelevantReferenceLabels(new[] { "Milo" });
        var still = sb.AddScene(8, "Milo posed for the card.", "Milo sitting on the windowsill", "none", SceneVisualType.AiImage);
        still.SetAllocation(20, null, "CTA: still image.");
        still.SetRelevantReferenceLabels(new[] { "Milo" });

        var (service, _, refs, _) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);

        var plan = await service.BuildAsync(_project);
        var videoScene = plan.Scenes.Single(s => s.GenerationType == "AI_VIDEO");
        var stillScene = plan.Scenes.Single(s => s.GenerationType == "AI_IMAGE");

        Assert.NotEqual(videoScene.FlowVideoPrompt, stillScene.ImagePrompt);
        Assert.Contains("(refer to the attached Milo reference)", videoScene.FlowVideoPrompt);
        // ComposeImagePrompt uses its own short "Milo, exactly as shown in the
        // attached reference image(s)" phrasing, not the video path's inline
        // "(refer to the attached {Name} reference)" tag shape.
        Assert.DoesNotContain("(refer to the attached Milo reference)", stillScene.ImagePrompt);
        Assert.StartsWith("A photorealistic vertical 9:16 photograph:", stillScene.ImagePrompt);
    }

    /// <summary>
    /// A Generated-but-not-yet-approved Keyframe must still surface a preview
    /// URL (the user needs to SEE it before deciding whether to approve) -
    /// only <see cref="FlowPlanScene.KeyframeApproved"/> is gated to the
    /// Approved status, unlike the Character/Environment anchors.
    /// </summary>
    [Fact]
    public async Task A_generated_but_unapproved_keyframe_still_exposes_a_preview_url()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo naps.", "Milo curled up on a cushion", "static", SceneVisualType.AiVideo);
        var keyframeAssetId = Guid.NewGuid();
        scene.MarkKeyframeGenerated(keyframeAssetId, "a photo of Milo napping");

        var (service, _, _, _) = Build(sb);
        var plan = await service.BuildAsync(_project);
        var planScene = plan.Scenes.Single(s => s.SceneId == scene.Id);

        Assert.Equal("Generated", planScene.KeyframeStatus);
        Assert.False(planScene.KeyframeApproved);
        Assert.Equal($"/content-projects/{_project}/assets/{keyframeAssetId}/file", planScene.KeyframeImageUrl);
    }

    [Fact]
    public async Task An_approved_keyframe_is_flagged_approved_and_a_hand_edited_motion_prompt_overrides_the_composed_one()
    {
        var sb = Storyboard.Create(_project);
        var scene = sb.AddScene(8, "Milo naps.", "Milo curled up on a cushion", "static", SceneVisualType.AiVideo);
        scene.MarkKeyframeGenerated(Guid.NewGuid(), "a photo of Milo napping");
        scene.ApproveKeyframe();
        scene.SetMotionPrompt("Milo's tail twitches slowly as he sleeps.");

        var (service, _, _, _) = Build(sb);
        var plan = await service.BuildAsync(_project);
        var planScene = plan.Scenes.Single(s => s.SceneId == scene.Id);

        Assert.Equal("Approved", planScene.KeyframeStatus);
        Assert.True(planScene.KeyframeApproved);
        Assert.Equal("Milo's tail twitches slowly as he sleeps.", planScene.MotionPrompt);
    }

    [Fact]
    public async Task A_scene_that_never_used_the_keyframe_workflow_has_no_keyframe_url_and_falls_back_to_the_composed_motion_only_prompt()
    {
        var (service, _, _, _) = Build();

        var plan = await service.BuildAsync(_project);
        var scene = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Equal("None", scene.KeyframeStatus);
        Assert.Null(scene.KeyframeImageUrl);
        Assert.False(scene.KeyframeApproved);
        // The first frame already fixes appearance/setting/style, so the motion
        // prompt describes only movement - never the full video prompt's style line.
        Assert.Contains("Starting exactly from the provided first frame", scene.MotionPrompt);
        Assert.DoesNotContain("vertical format", scene.MotionPrompt);
        Assert.NotEqual(scene.FlowVideoPrompt, scene.MotionPrompt);
    }

    // --- CharacterBehaviorProfiles wiring through the real service (both
    //     BuildNarrative/FlowVideoPrompt and Compose/ImagePrompt) ----------

    private static Storyboard StoryboardWithCatScene(Guid project, string action, IReadOnlyList<string> tags)
    {
        var sb = Storyboard.Create(project);
        var video = sb.AddScene(8, "narration", action, "slow push in", SceneVisualType.AiVideo);
        video.SetAllocation(100, "Fast", "Hero clip.");
        video.SetRelevantReferenceLabels(tags);
        var still = sb.AddScene(8, "narration", action, "none", SceneVisualType.AiImage);
        still.SetAllocation(20, null, "CTA: still image.");
        still.SetRelevantReferenceLabels(tags);
        return sb;
    }

    [Fact]
    public async Task Flow_video_and_image_prompts_both_include_the_clause_for_a_cat_flagged_character_with_a_matching_action()
    {
        var sb = StoryboardWithCatScene(_project, "Milo sits upright on the windowsill and watches the street", new[] { "Milo" });
        var (service, _, refs, storyVisualContext) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        storyVisualContext.CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat };

        var plan = await service.BuildAsync(_project);
        var video = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");
        var still = plan.Scenes.First(s => s.GenerationType == "AI_IMAGE");

        Assert.Contains("anthropomorphically", video.FlowVideoPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anthropomorphically", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Flow_video_and_image_prompts_have_no_clause_when_the_character_is_not_opted_in()
    {
        var sb = StoryboardWithCatScene(_project, "Milo sits upright on the windowsill and watches the street", new[] { "Milo" });
        var (service, _, refs, storyVisualContext) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        storyVisualContext.CharacterBehaviorProfiles = null; // not a Story-linked project / no character opted in

        var plan = await service.BuildAsync(_project);
        var video = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");
        var still = plan.Scenes.First(s => s.GenerationType == "AI_IMAGE");

        Assert.DoesNotContain("anthropomorphically", video.FlowVideoPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anthropomorphically", still.ImagePrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mixed_scene_only_the_flagged_characters_clause_reaches_the_flow_and_image_prompts()
    {
        var sb = StoryboardWithCatScene(
            _project, "Milo sits upright on the stool while Rex stands quietly nearby", new[] { "Milo", "Rex" });
        var (service, _, refs, storyVisualContext) = Build(sb);
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);
        var rex = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/rex.png", null, "upload", label: "Rex");
        rex.Approve();
        refs.Rows.Add(rex);
        storyVisualContext.CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile>
        {
            ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat,
            ["Rex"] = CharacterBehaviorProfile.None,
        };

        var plan = await service.BuildAsync(_project);
        var video = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");
        var still = plan.Scenes.First(s => s.GenerationType == "AI_IMAGE");

        foreach (var prompt in new[] { video.FlowVideoPrompt, still.ImagePrompt })
        {
            Assert.Contains("Milo", prompt);
            Assert.Contains("Rex", prompt);
            var occurrences = System.Text.RegularExpressions.Regex.Matches(prompt, "anthropomorphically").Count;
            Assert.Equal(1, occurrences);
        }
    }

    [Fact]
    public async Task Behavior_clause_is_governed_only_by_BehaviorProfile_never_by_the_projects_Niche_free_text()
    {
        // ContentProject.Niche is unrelated free text with no bearing on the
        // scoped behavior-clause feature - an arbitrary/unusual Niche value
        // must not change whether the clause appears.
        var sb = StoryboardWithCatScene(_project, "Milo sits upright on the windowsill and watches the street", new[] { "Milo" });
        var project = ContentProject.Create("Odd Niche Project", "topic", "underwater basket weaving tutorials", 60, "9:16", "en");
        var attemptRepo = new InMemoryGenerationAttemptRepository();
        var ledger = new CreditLedgerService(attemptRepo, Options.Create(new CreditCostOptions()), NullLogger<CreditLedgerService>.Instance);
        var refs = new FakeAssetReferenceRepository();
        var storyVisualContext = new FakeStoryVisualContextResolver
        {
            CharacterBehaviorProfiles = new Dictionary<string, CharacterBehaviorProfile> { ["Milo"] = CharacterBehaviorProfile.AnthropomorphicCat },
        };
        var milo = AssetReference.CreateGenerated(_project, AssetReferenceType.Character, "path/milo.png", null, "upload", label: "Milo");
        milo.Approve();
        refs.Rows.Add(milo);

        var service = new FlowGenerationPlanService(
            new FakeContentProjectRepository(project),
            new FakeStoryboardRepository(sb),
            refs,
            ledger,
            storyVisualContext,
            Options.Create(new CreditCostOptions()),
            Options.Create(new FlowModelOptions()));

        var plan = await service.BuildAsync(_project);
        var video = plan.Scenes.First(s => s.GenerationType == "AI_VIDEO");

        Assert.Contains("anthropomorphically", video.FlowVideoPrompt, StringComparison.OrdinalIgnoreCase);
        // The unusual Niche text itself never leaks into the prompt either.
        Assert.DoesNotContain("basket weaving", video.FlowVideoPrompt, StringComparison.OrdinalIgnoreCase);
    }
}
