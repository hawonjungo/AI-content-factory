using System.Net;
using System.Text.Json;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Infrastructure.Providers.Gemini;
using AiContentFactory.Infrastructure.Providers.Kokoro;
using AiContentFactory.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// The free Kokoro voices: routed by voice id, $0, English only, and - the
/// cost-safety rule - never silently swapped for a paid Gemini voice.
/// No real network: HTTP is a fake handler.
/// </summary>
public class FreeVoiceTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }
        public Func<HttpResponseMessage> Respond { get; set; } =
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(WavTestData.Pcm16(24000, 1.0, 8000)) };
        public bool Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Fail)
            {
                throw new HttpRequestException("connection refused");
            }

            return Respond();
        }
    }

    private static (KokoroTtsProvider Provider, Handler Handler) Kokoro(string baseUrl = "http://kokoro:8880")
    {
        var handler = new Handler();
        var provider = new KokoroTtsProvider(
            new HttpClient(handler),
            Options.Create(new KokoroOptions { BaseUrl = baseUrl }),
            NullLogger<KokoroTtsProvider>.Instance);
        return (provider, handler);
    }

    [Fact]
    public async Task A_kokoro_voice_calls_the_local_server_and_is_free()
    {
        var (provider, handler) = Kokoro();

        var result = await provider.GenerateAsync(new TtsRequest("Hello there.", "kokoro:am_michael", SpeakingRate: 1.2, Language: "en-US"));

        Assert.True(result.IsFree);
        Assert.Equal("http://kokoro:8880/v1/audio/speech", handler.Last!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("am_michael", body.RootElement.GetProperty("voice").GetString());
        Assert.Equal("wav", body.RootElement.GetProperty("response_format").GetString());
        Assert.Equal(1.2, body.RootElement.GetProperty("speed").GetDouble(), 3);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("vi")]
    public async Task A_non_english_script_is_refused_before_any_request(string language)
    {
        var (provider, handler) = Kokoro();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GenerateAsync(new TtsRequest("Xin chào.", "kokoro:af_heart", Language: language)));

        Assert.Contains("tiếng Anh", ex.Message);
        Assert.Null(handler.Last);
    }

    [Fact]
    public async Task An_unreachable_or_unconfigured_server_is_a_clear_error_never_a_paid_fallback()
    {
        var (down, handler) = Kokoro();
        handler.Fail = true;
        var unreachable = await Assert.ThrowsAsync<InvalidOperationException>(() => down.GenerateAsync(new TtsRequest("Hi.", "kokoro:af_heart")));
        Assert.Contains("Kokoro", unreachable.Message);

        var (off, offHandler) = Kokoro(baseUrl: "");
        var unconfigured = await Assert.ThrowsAsync<InvalidOperationException>(() => off.GenerateAsync(new TtsRequest("Hi.", "kokoro:af_heart")));
        Assert.Contains("free-tts", unconfigured.Message);
        Assert.Null(offHandler.Last);
    }

    [Theory]
    [InlineData("kokoro:af_heart", true)]
    [InlineData("KOKORO:bm_george", true)]
    [InlineData("Kore", false)]
    [InlineData(null, false)]
    public void Only_kokoro_prefixed_voices_are_routed_to_the_free_server(string? voice, bool free)
    {
        Assert.Equal(free, RoutingTtsProvider.IsKokoroVoice(voice));
    }

    [Fact]
    public void A_gender_switch_keeps_a_free_voice_free_and_a_paid_voice_paid()
    {
        var freeFemale = PresetCatalog.Voices.First(v => v.IsFree && v.Gender == VoiceGender.Female);
        var paidFemale = PresetCatalog.Voices.First(v => !v.IsFree && v.Gender == VoiceGender.Female);

        var fromFree = PresetCatalog.ResolveVoice(freeFemale.Id, VoiceGender.Male);
        var fromPaid = PresetCatalog.ResolveVoice(paidFemale.Id, VoiceGender.Male);

        Assert.True(fromFree.IsFree);
        Assert.Equal(VoiceGender.Male, fromFree.Gender);
        Assert.False(fromPaid.IsFree);
        Assert.Equal(VoiceGender.Male, fromPaid.Gender);
    }

    [Fact]
    public void Projects_without_a_chosen_voice_never_land_on_a_free_voice()
    {
        Assert.False(PresetCatalog.ResolveVoice(null, VoiceGender.Male).IsFree);
        Assert.False(PresetCatalog.ResolveVoice(null, VoiceGender.Female).IsFree);
        Assert.False(PresetCatalog.ResolveVoice(null, VoiceGender.Unspecified).IsFree);
        Assert.All(PresetCatalog.Voices.Where(v => v.IsFree), v => Assert.StartsWith(KokoroTtsProvider.VoicePrefix, v.GeminiVoiceName));
    }

    [Fact]
    public async Task The_estimate_prices_gemini_narration_and_shows_zero_for_a_free_voice()
    {
        var paid = ContentProject.Create("Paid", "topic", "storytelling", 60, "9:16", "en");
        var free = ContentProject.Create("Free", "topic", "storytelling", 60, "9:16", "en");
        free.ApplyPresets(null, null, "free-en-female-us", null, null);

        decimal NarrationCost(AiContentFactory.Application.Costs.GenerationEstimate e) =>
            e.LineItems.Single(i => i.Label.StartsWith("Lồng tiếng")).EstimatedCostUsd;

        async Task<AiContentFactory.Application.Costs.GenerationEstimate> EstimateAsync(ContentProject project)
        {
            var storyboard = AiContentFactory.Domain.Storyboards.Storyboard.Create(project.Id);
            storyboard.AddScene(8, new string('a', 1000), "", "", AiContentFactory.Domain.Storyboards.SceneVisualType.AiImage);
            var estimator = new AiContentFactory.Application.Costs.GenerationEstimator(
                new FakeStoryboardService(new FakeStoryboardRepository(storyboard)),
                new FakeAssetService(),
                new FakeAiUsageTracker(),
                new FakeGoogleFlowQuotaManager(),
                Options.Create(new AiContentFactory.Application.Costs.PricingOptions()),
                Options.Create(new AiContentFactory.Application.Costs.BudgetOptions()),
                Options.Create(new AiContentFactory.Application.Generation.GoogleFlowOptions()),
                NullLogger<AiContentFactory.Application.Costs.GenerationEstimator>.Instance,
                new FakeContentProjectRepository(project));
            return await estimator.EstimateProjectAsync(project.Id);
        }

        Assert.Equal(0.02m, NarrationCost(await EstimateAsync(paid))); // 1,000 chars at $0.02 / 1,000
        Assert.Equal(0m, NarrationCost(await EstimateAsync(free)));
    }
}
