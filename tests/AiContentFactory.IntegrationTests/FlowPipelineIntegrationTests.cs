using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Audio;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Generation;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Application.Tts;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiContentFactory.IntegrationTests;

public class FlowPipelineIntegrationTests : IClassFixture<PostgresHostFixture>
{
    private readonly PostgresHostFixture _host;

    public FlowPipelineIntegrationTests(PostgresHostFixture host) => _host = host;

    [Fact]
    public async Task Pre_flow_assets_then_flow_clip_import_then_plan_on_real_postgres()
    {
        if (!_host.Available)
        {
            // Opt-in hard failure so CI can insist the integration test actually ran.
            Assert.False(Environment.GetEnvironmentVariable("REQUIRE_INTEGRATION") == "1",
                _host.SkipReason ?? "integration host unavailable");
            return;
        }

        Guid projectId;
        int plannedCredits;

        // --- PRE-FLOW: project -> script (fake LLM) -> storyboard -> narration (fake TTS) + persisted AudioTiming
        await using (var scope = _host.Scope())
        {
            var sp = scope.ServiceProvider;

            var projects = sp.GetRequiredService<IContentProjectService>();
            var created = await projects.CreateAsync(new CreateContentProjectRequest(
                "Office Cat", "why cats love laptops", "storytelling", 60, "9:16", "en", TemplateId: "faceless-story"));
            projectId = created.Id;

            await sp.GetRequiredService<IContentPipelineService>().RunAsync(projectId);
            await sp.GetRequiredService<IClipPlanService>()
                .GenerateAsync(projectId, new GenerateClipPlanRequest(3, 8, ClipPlanStrategy.AllVideo));

            foreach (var s in new[] { ContentProjectStatus.StoryboardReady, ContentProjectStatus.Generating, ContentProjectStatus.Editing })
            {
                await projects.ChangeStatusAsync(projectId, new ChangeContentProjectStatusRequest(s));
            }

            var storyboardSvc = sp.GetRequiredService<IStoryboardService>();
            var storyboard = await storyboardSvc.GetOrCreateAsync(projectId);
            var tts = sp.GetRequiredService<ITtsService>();
            var timing = sp.GetRequiredService<IAudioTimingService>();
            var storage = sp.GetRequiredService<IFileStorage>();
            var assets = sp.GetRequiredService<IAssetService>();
            var voice = PresetCatalog.ResolveVoice(null, VoiceGender.Unspecified);

            foreach (var scene in storyboard.Scenes)
            {
                var audio = await tts.SynthesizeAsync(new TtsSynthesisRequest(scene.Narration, VoiceProfile.FromPreset(voice)));
                var path = await storage.SaveAsync($"content-projects/{projectId}/scenes/{scene.Id}/voice.wav", audio.AudioBytes);
                await assets.CreateAsync(projectId, new CreateAssetRequest(scene.Id, AssetType.Voice, "fake-tts", scene.Narration, path, audio.DurationSeconds, null, null));
                await storyboardSvc.SetSceneAudioTimingAsync(projectId, scene.Id,
                    timing.Serialize(timing.Compute(scene.Narration, audio.DurationSeconds)));
            }

            Assert.Equal(3, storyboard.Scenes.Count);
            Assert.All(storyboard.Scenes, s => Assert.Equal("AI_VIDEO", s.GenerationType));

            // The Flow plan lists all 3 scenes as still needing a clip, BEFORE any import.
            var prePlan = await sp.GetRequiredService<IFlowGenerationPlanService>().BuildAsync(projectId);
            plannedCredits = prePlan.PlannedCredits;
            Assert.Equal(3, prePlan.ScenesRequiringFlow);
            Assert.True(plannedCredits > 0);
        }

        // --- POST-FLOW: import a validated clip per AI_VIDEO scene (fake probe, no ffmpeg)
        await using (var scope = _host.Scope())
        {
            var sp = scope.ServiceProvider;
            var import = sp.GetRequiredService<IFlowClipImportService>();
            var storyboard = await sp.GetRequiredService<IStoryboardService>().GetOrCreateAsync(projectId);

            _host.Probe.Next = new MediaInfo(true, null, 8.0, 1080, 1920, 30, HasVideo: true, HasAudio: false, AudioDurationSeconds: 0);

            foreach (var scene in storyboard.Scenes.Where(s => s.GenerationType == "AI_VIDEO"))
            {
                using var clip = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
                var result = await import.ImportAsync(projectId, scene.Id, "flow-clip.mp4", clip);
                Assert.True(result.Accepted, string.Join("; ", result.Issues));
                Assert.Equal("9:16", result.AspectLabel);
            }

            var status = await import.GetStatusAsync(projectId);
            Assert.Equal(3, status.ScenesNeedingFlow);
            Assert.Equal(3, status.ImportedValid);
            Assert.Equal(0, status.MissingOrInvalid);
            Assert.True(status.ReadyForRender);
        }

        // --- READ BACK: plan, credit ledger, persisted timing, scene state - all from Postgres
        await using (var scope = _host.Scope())
        {
            var sp = scope.ServiceProvider;

            // Every AI_VIDEO scene now has an imported clip, so the plan no longer
            // asks the user to generate anything in Flow (and books no more credits).
            var plan = await sp.GetRequiredService<IFlowGenerationPlanService>().BuildAsync(projectId);
            Assert.Equal(0, plan.ScenesRequiringFlow);
            Assert.Equal(0, plan.PlannedCredits);
            Assert.Equal(string.Empty, plan.CopyAllText);

            var usage = await sp.GetRequiredService<ICreditLedger>().GetDailyUsageAsync();
            Assert.Equal(plannedCredits, usage.Used); // the 3 imported Flow clips were booked, once each

            var storyboard = await sp.GetRequiredService<IStoryboardService>().GetOrCreateAsync(projectId);
            Assert.All(storyboard.Scenes, s => Assert.False(string.IsNullOrWhiteSpace(s.AudioTimingJson)));
            Assert.All(storyboard.Scenes, s => Assert.Equal("Generated", s.Status));
            Assert.All(storyboard.Scenes, s => Assert.True(s.SkipGeneration)); // Phase 7: imported clips lock out generation

            var project = await sp.GetRequiredService<IContentProjectService>().GetByIdAsync(projectId);
            Assert.Equal(nameof(ContentProjectStatus.Editing), project!.Status);
        }

        // --- IDEMPOTENCE: re-importing does not double-charge
        await using (var scope = _host.Scope())
        {
            var sp = scope.ServiceProvider;
            var import = sp.GetRequiredService<IFlowClipImportService>();
            var storyboard = await sp.GetRequiredService<IStoryboardService>().GetOrCreateAsync(projectId);
            var first = storyboard.Scenes.First(s => s.GenerationType == "AI_VIDEO");

            using var clip = new MemoryStream(new byte[] { 9, 9, 9 });
            await import.ImportAsync(projectId, first.Id, "again.mp4", clip);

            var usage = await sp.GetRequiredService<ICreditLedger>().GetDailyUsageAsync();
            Assert.Equal(plannedCredits, usage.Used);
        }
    }
}
