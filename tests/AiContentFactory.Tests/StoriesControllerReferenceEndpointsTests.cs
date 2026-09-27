using AiContentFactory.Api.Controllers;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Stories;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Stories;
using AiContentFactory.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// HTTP-level mapping of the Story character reference endpoints (status codes,
/// query parsing, upload plumbing). The real <see cref="StoryAssetReferenceService"/>
/// runs on fakes - no provider, database or network.
/// </summary>
public class StoriesControllerReferenceEndpointsTests
{
    private sealed class StubProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode ?? 500, Title = title, Detail = detail };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext, ModelStateDictionary modelStateDictionary, int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode ?? 400, Title = title };
    }

    private sealed class ExhaustedUsageTracker : IAiUsageTracker
    {
        public bool Exhausted { get; set; }
        public Task RecordAsync(RecordUsageInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<decimal> GetCurrentMonthSpendAsync(CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task EnsureBudgetAvailableAsync(CancellationToken cancellationToken = default) =>
            Exhausted ? throw new BudgetExceededException(5m, 5m) : Task.CompletedTask;
    }

    private sealed class OpenLedger : ICreditLedger
    {
        public Task EnsureAvailableAsync(int requiredCredits, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> GetRemainingDailyCreditsAsync(CancellationToken cancellationToken = default) => Task.FromResult(100);
        public Task<CreditUsageSummary> GetDailyUsageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiContentFactory.Domain.Generation.GenerationAttempt> ReserveAsync(CreditReservationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CommitAsync(Guid attemptId, int actualCredits, Guid? assetId = null, double? audioDurationSeconds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiContentFactory.Domain.Generation.GenerationAttempt> RecordExternalCompletionAsync(CreditReservationRequest request, int actualCredits, Guid? assetId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkValidatedAsync(Guid attemptId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task FailAsync(Guid attemptId, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record Fixture(
        StoriesController Controller,
        FakeStoryRepository Repo,
        FakeFileStorage Storage,
        FakeImageGenerationProvider Provider,
        ExhaustedUsageTracker Usage,
        Story Story,
        StoryCharacter Character,
        StoryReferenceGenerationGate Gate);

    private static Fixture Build(string? description = "A witty fox detective.")
    {
        var repo = new FakeStoryRepository();
        var storage = new FakeFileStorage();
        var provider = new FakeImageGenerationProvider();
        var usage = new ExhaustedUsageTracker();
        var gate = new StoryReferenceGenerationGate();
        var service = new StoryAssetReferenceService(
            repo, provider, storage,
            new FakeMediaProbe(FakeMediaProbe.GoodInfo(duration: 0.04, w: 1024, h: 1536, fps: 25)),
            usage, new OpenLedger(), gate,
            Options.Create(new PricingOptions()), Options.Create(new CreditCostOptions()),
            NullLogger<StoryAssetReferenceService>.Instance);

        var story = Story.Create("My Series", null, null, null, null);
        var character = StoryCharacter.Create(story.Id, "Nova", description, description);
        story.AttachCharacter(character);
        repo.Stories[story.Id] = story;
        repo.Characters[character.Id] = character;

        var controller = new StoriesController(null!, null!, null!, service, repo, storage, NullLogger<StoriesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new StubProblemDetailsFactory()
        };
        return new Fixture(controller, repo, storage, provider, usage, story, character, gate);
    }

    private static int StatusOf(IActionResult? result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        FileResult => 200,
        _ => throw new InvalidOperationException($"Unexpected result {result?.GetType().Name}")
    };

    private static IFormFile FormFileOf(byte[] bytes, string fileName) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);

    private static async Task<StoryCharacter> ApproveAsync(Fixture f)
    {
        f.Storage.Files["stories/x/approved.png"] = new byte[] { 1 };
        f.Character.SetReferenceCandidate("stories/x/approved.png", "p", "gemini", ReferenceImageSource.Generated);
        var result = await f.Controller.ApproveCharacterReferenceImage(f.Story.Id, f.Character.Id, confirmReplace: false, expectedPendingVersion: null, CancellationToken.None);
        Assert.Equal(200, StatusOf(result.Result));
        return f.Character;
    }

    // ---- GET reference-prompt ----

    [Theory]
    [InlineData(null, "generic")]
    [InlineData("", "generic")]
    [InlineData("inapp", "inapp")]
    [InlineData("MIDJOURNEY", "midjourney")]
    [InlineData("Flow", "flow")]
    [InlineData("dalle", "dalle")]
    [InlineData("FLUX", "flux")]
    public async Task Reference_prompt_accepts_every_target_case_insensitively_and_defaults_to_generic(string? target, string expectedTarget)
    {
        var f = Build();

        var result = await f.Controller.GetCharacterReferencePrompt(f.Story.Id, f.Character.Id, target, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<CharacterReferencePromptResponse>(ok.Value);
        Assert.Equal(expectedTarget, body.Target);
        Assert.False(string.IsNullOrWhiteSpace(body.Prompt));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task Reference_prompt_rejects_an_unknown_target_with_400()
    {
        var f = Build();

        var result = await f.Controller.GetCharacterReferencePrompt(f.Story.Id, f.Character.Id, "sora", CancellationToken.None);

        Assert.Equal(400, StatusOf(result.Result));
    }

    [Fact]
    public async Task Reference_prompt_for_an_unknown_character_is_404_and_for_a_not_ready_character_still_returns_the_prompt()
    {
        var f = Build(description: null);

        var missing = await f.Controller.GetCharacterReferencePrompt(f.Story.Id, Guid.NewGuid(), "generic", CancellationToken.None);
        var notReady = await f.Controller.GetCharacterReferencePrompt(f.Story.Id, f.Character.Id, "generic", CancellationToken.None);

        Assert.IsType<NotFoundResult>(missing.Result);
        var body = Assert.IsType<CharacterReferencePromptResponse>(Assert.IsType<OkObjectResult>(notReady.Result).Value);
        Assert.Contains("appearance", body.MissingFields);
    }

    // ---- POST reference-image (generate) ----

    [Fact]
    public async Task Generate_returns_400_for_a_character_that_is_not_ready_and_makes_no_provider_call()
    {
        var f = Build(description: null);

        var result = await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);

        Assert.Equal(400, StatusOf(result.Result));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task Generate_maps_an_exhausted_budget_to_429_instead_of_a_500()
    {
        var f = Build();
        f.Usage.Exhausted = true;

        var result = await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);

        Assert.Equal(429, StatusOf(result.Result));
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task Generate_returns_the_extended_response_for_a_ready_character()
    {
        var f = Build();

        var result = await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);

        var body = Assert.IsType<StoryReferenceImageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Generated", body.Status);
        Assert.False(body.HasPending);
        Assert.Equal("Generated", body.Source);
    }

    // ---- PUT approve?confirmReplace ----

    [Fact]
    public async Task Approving_a_candidate_over_an_approved_image_is_409_without_confirmReplace_and_200_with_it()
    {
        var f = Build();
        await ApproveAsync(f);
        await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);
        Assert.True(f.Character.HasPendingReferenceImage);

        var conflict = await f.Controller.ApproveCharacterReferenceImage(f.Story.Id, f.Character.Id, confirmReplace: false, expectedPendingVersion: null, CancellationToken.None);

        Assert.Equal(409, StatusOf(conflict.Result));
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(conflict.Result).Value);
        Assert.Equal("REFERENCE_REPLACE_CONFIRMATION_REQUIRED", problem.Extensions["code"]?.ToString());
        Assert.Equal("stories/x/approved.png", f.Character.ReferenceImagePath);

        var confirmed = await f.Controller.ApproveCharacterReferenceImage(f.Story.Id, f.Character.Id, confirmReplace: true, expectedPendingVersion: null, CancellationToken.None);

        var body = Assert.IsType<StoryReferenceImageResponse>(Assert.IsType<OkObjectResult>(confirmed.Result).Value);
        Assert.False(body.HasPending);
        Assert.NotEqual("stories/x/approved.png", body.ImagePath);
    }

    [Fact]
    public async Task Approving_without_any_generated_image_is_a_plain_400()
    {
        var f = Build();

        var result = await f.Controller.ApproveCharacterReferenceImage(f.Story.Id, f.Character.Id, confirmReplace: false, expectedPendingVersion: null, CancellationToken.None);

        Assert.Equal(400, StatusOf(result.Result));
    }

    [Fact]
    public async Task Approve_with_a_stale_expectedPendingVersion_is_409_REFERENCE_PENDING_CHANGED_and_promotes_nothing()
    {
        var f = Build();
        await ApproveAsync(f);
        await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);
        var pending = f.Character.PendingReferenceImagePath;

        var result = await f.Controller.ApproveCharacterReferenceImage(
            f.Story.Id, f.Character.Id, confirmReplace: true, expectedPendingVersion: "0123456789ab", CancellationToken.None);

        Assert.Equal(409, StatusOf(result.Result));
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result.Result).Value);
        Assert.Equal("REFERENCE_PENDING_CHANGED", problem.Extensions["code"]?.ToString());
        Assert.Equal("stories/x/approved.png", f.Character.ReferenceImagePath);
        Assert.Equal(pending, f.Character.PendingReferenceImagePath);
    }

    [Fact]
    public async Task Approve_with_the_current_expectedPendingVersion_succeeds_and_an_omitted_one_is_the_legacy_behaviour()
    {
        var f = Build();
        await ApproveAsync(f);
        var generated = Assert.IsType<StoryReferenceImageResponse>(Assert.IsType<OkObjectResult>(
            (await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None)).Result).Value);
        Assert.NotNull(generated.PendingVersion);

        var confirmed = await f.Controller.ApproveCharacterReferenceImage(
            f.Story.Id, f.Character.Id, confirmReplace: true, expectedPendingVersion: generated.PendingVersion, CancellationToken.None);

        var body = Assert.IsType<StoryReferenceImageResponse>(Assert.IsType<OkObjectResult>(confirmed.Result).Value);
        Assert.False(body.HasPending);
        Assert.Null(body.PendingVersion);

        // Omitted: today's behaviour (a fresh Generated image approves with no version and no confirmation).
        var f2 = Build();
        await f2.Controller.GenerateCharacterReferenceImage(f2.Story.Id, f2.Character.Id, CancellationToken.None);
        var legacy = await f2.Controller.ApproveCharacterReferenceImage(f2.Story.Id, f2.Character.Id, confirmReplace: false, expectedPendingVersion: null, CancellationToken.None);
        Assert.Equal(200, StatusOf(legacy.Result));
    }

    // ---- 409 while another generation/upload is in flight ----

    [Fact]
    public async Task Generate_and_upload_are_409_REFERENCE_OPERATION_IN_PROGRESS_while_one_is_already_running()
    {
        var f = Build();

        using (f.Gate.TryEnter(f.Character.Id))
        {
            var generate = await f.Controller.GenerateCharacterReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);
            var upload = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, FormFileOf(PngBytes.OnePixel(), "a.png"), CancellationToken.None);

            foreach (var result in new[] { generate.Result, upload.Result })
            {
                Assert.Equal(409, StatusOf(result));
                var problem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
                Assert.Equal("REFERENCE_OPERATION_IN_PROGRESS", problem.Extensions["code"]?.ToString());
            }
        }

        Assert.Equal(0, f.Provider.Calls);
        Assert.Empty(f.Storage.Files);
    }

    // ---- POST reference-image/upload ----

    [Fact]
    public async Task Upload_accepts_a_valid_image_as_a_candidate_and_never_calls_the_provider()
    {
        var f = Build();

        var result = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, FormFileOf(PngBytes.OnePixel(), "nova.png"), CancellationToken.None);

        var body = Assert.IsType<StoryReferenceImageResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Uploaded", body.Source);
        Assert.Equal("user-upload", body.Provider);
        Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task Upload_rejects_a_missing_or_empty_file_a_bad_extension_and_non_image_content_with_400()
    {
        var f = Build();

        var none = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, null!, CancellationToken.None);
        var empty = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, FormFileOf(Array.Empty<byte>(), "a.png"), CancellationToken.None);
        var badExtension = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, FormFileOf(PngBytes.OnePixel(), "a.webp"), CancellationToken.None);
        var notAnImage = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, FormFileOf(new byte[] { 1, 2, 3, 4 }, "a.png"), CancellationToken.None);

        Assert.Equal(400, StatusOf(none.Result));
        Assert.Equal(400, StatusOf(empty.Result));
        Assert.Equal(400, StatusOf(badExtension.Result));
        Assert.Equal(400, StatusOf(notAnImage.Result));
        Assert.Empty(f.Storage.Files);
    }

    [Fact]
    public async Task Upload_for_an_unknown_character_is_404()
    {
        var f = Build();

        var result = await f.Controller.UploadCharacterReferenceImage(f.Story.Id, Guid.NewGuid(), FormFileOf(PngBytes.OnePixel(), "a.png"), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    // ---- DELETE pending / GET pending file ----

    [Fact]
    public async Task The_pending_file_is_served_only_while_a_candidate_exists_and_discard_removes_it()
    {
        var f = Build();
        await ApproveAsync(f);

        var none = await f.Controller.GetCharacterPendingReferenceImageFile(f.Story.Id, f.Character.Id, CancellationToken.None);
        Assert.IsType<NotFoundResult>(none);

        await f.Controller.UploadCharacterReferenceImage(f.Story.Id, f.Character.Id, FormFileOf(PngBytes.OnePixel(), "new.png"), CancellationToken.None);
        var served = await f.Controller.GetCharacterPendingReferenceImageFile(f.Story.Id, f.Character.Id, CancellationToken.None);
        var file = Assert.IsType<FileStreamResult>(served);
        Assert.Equal("image/png", file.ContentType);

        var discarded = await f.Controller.DiscardCharacterPendingReferenceImage(f.Story.Id, f.Character.Id, CancellationToken.None);

        var body = Assert.IsType<StoryReferenceImageResponse>(Assert.IsType<OkObjectResult>(discarded.Result).Value);
        Assert.False(body.HasPending);
        Assert.Equal("Approved", body.Status);
        Assert.IsType<NotFoundResult>(await f.Controller.GetCharacterPendingReferenceImageFile(f.Story.Id, f.Character.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Discard_for_an_unknown_character_is_404()
    {
        var f = Build();

        var result = await f.Controller.DiscardCharacterPendingReferenceImage(f.Story.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
