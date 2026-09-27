using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Stories;
using AiContentFactory.Application.Stories.Continuity;
using AiContentFactory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace AiContentFactory.Api.Controllers;

/// <summary>
/// CRUD for the Story/Series "bible" (Story + its Characters/Locations/
/// Episodes/State), plus the AI generation workflow layered on top
/// (bible/outline/script/validate/finalize - all delegated to
/// <see cref="IStoryContinuityManager"/>, which is the only place real
/// generation logic lives). No `/api` prefix, matching every other
/// controller in this app (e.g. ContentProjectsController's "content-projects").
/// </summary>
[ApiController]
[Route("stories")]
public class StoriesController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>Request cap for a reference-image upload: the service's 10 MB file cap plus multipart overhead.</summary>
    private const int MaxReferenceUploadRequestBytes = StoryAssetReferenceService.MaxUploadBytes + 1024 * 1024;

    private readonly IStoryService _service;
    private readonly IStoryContinuityManager _continuityManager;
    private readonly IStoryVideoLinkService _videoLinkService;
    private readonly IStoryAssetReferenceService _assetReferenceService;
    private readonly IStoryRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<StoriesController> _logger;

    public StoriesController(
        IStoryService service,
        IStoryContinuityManager continuityManager,
        IStoryVideoLinkService videoLinkService,
        IStoryAssetReferenceService assetReferenceService,
        IStoryRepository repository,
        IFileStorage fileStorage,
        ILogger<StoriesController> logger)
    {
        _service = service;
        _continuityManager = continuityManager;
        _videoLinkService = videoLinkService;
        _assetReferenceService = assetReferenceService;
        _repository = repository;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StoryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var stories = await _service.GetAllAsync(cancellationToken);
        return Ok(stories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StoryResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var story = await _service.GetByIdAsync(id, cancellationToken);
        return story is null ? NotFound() : Ok(story);
    }

    [HttpPost]
    public async Task<ActionResult<StoryResponse>> Create([FromBody] CreateStoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateAsync(request, cancellationToken);
            _logger.LogInformation("Created Story {StoryId} - {Title}", created.Id, created.Title);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StoryResponse>> Update(Guid id, [FromBody] UpdateStoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _service.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPost("{id:guid}/episodes")]
    public async Task<ActionResult<StoryEpisodeResponse>> CreateEpisode(Guid id, [FromBody] CreateStoryEpisodeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateEpisodeAsync(id, request, cancellationToken);
            if (created is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Created StoryEpisode {EpisodeId} (#{EpisodeNumber}) for Story {StoryId}", created.Id, created.EpisodeNumber, id);
            return CreatedAtAction(nameof(GetEpisodeById), new { id, episodeId = created.Id }, created);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpGet("{id:guid}/episodes")]
    public async Task<ActionResult<IReadOnlyList<StoryEpisodeSummaryResponse>>> GetEpisodes(Guid id, CancellationToken cancellationToken)
    {
        var episodes = await _service.GetEpisodesAsync(id, cancellationToken);
        return episodes is null ? NotFound() : Ok(episodes);
    }

    [HttpGet("{id:guid}/episodes/{episodeId:guid}")]
    public async Task<ActionResult<StoryEpisodeResponse>> GetEpisodeById(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        var episode = await _service.GetEpisodeByIdAsync(id, episodeId, cancellationToken);
        return episode is null ? NotFound() : Ok(episode);
    }

    /// <summary>
    /// Manual/human script edit - a plain CRUD mutation (no AI call),
    /// distinct from POST .../script which always regenerates via the
    /// writer agent. Follows the same PUT pattern as <see cref="Update"/>.
    /// </summary>
    [HttpPut("{id:guid}/episodes/{episodeId:guid}/script")]
    public async Task<ActionResult<StoryEpisodeResponse>> UpdateEpisodeScript(Guid id, Guid episodeId, [FromBody] UpdateStoryEpisodeScriptRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var episode = await _service.SetEpisodeScriptAsync(id, episodeId, request, cancellationToken);
            return episode is null ? NotFound() : Ok(episode);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPost("{id:guid}/characters")]
    public async Task<ActionResult<StoryCharacterResponse>> CreateCharacter(Guid id, [FromBody] CreateStoryCharacterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateCharacterAsync(id, request, cancellationToken);
            if (created is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Created StoryCharacter {CharacterId} for Story {StoryId}", created.Id, id);
            return Ok(created);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPut("{id:guid}/characters/{characterId:guid}")]
    public async Task<ActionResult<StoryCharacterResponse>> UpdateCharacter(Guid id, Guid characterId, [FromBody] UpdateStoryCharacterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _service.UpdateCharacterAsync(id, characterId, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpPost("{id:guid}/locations")]
    public async Task<ActionResult<StoryLocationResponse>> CreateLocation(Guid id, [FromBody] CreateStoryLocationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateLocationAsync(id, request, cancellationToken);
            if (created is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Created StoryLocation {LocationId} for Story {StoryId}", created.Id, id);
            return Ok(created);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    // ---- Story-level Character/Location reference images ----
    // Generated once, reused across every episode via IStoryVideoLinkService's
    // seeding step (see StoryAssetReferenceService's own remarks). This is a
    // billable image-generation call (per CLAUDE.md Section 10/23) but does
    // NOT go through JsonAgentRunner/an LLM, so there is no AgentGenerationException
    // to catch here - a provider failure propagates as an unhandled exception,
    // same as it would have before this feature existed.

    /// <summary>
    /// The composed reference prompt for one export target (inapp|generic|flow|midjourney|dalle|flux,
    /// case-insensitive, default generic). No AI call and no cost, and it does not require the
    /// character to be generation-ready - it returns what it can plus the advisory missingFields.
    /// </summary>
    [HttpGet("{id:guid}/characters/{characterId:guid}/reference-prompt")]
    public async Task<ActionResult<CharacterReferencePromptResponse>> GetCharacterReferencePrompt(Guid id, Guid characterId, [FromQuery] string? target, CancellationToken cancellationToken)
    {
        var parsedTarget = ReferencePromptTarget.Generic;
        if (!string.IsNullOrWhiteSpace(target) && !CharacterReferencePromptComposer.TryParseTarget(target, out parsedTarget))
        {
            return ValidationProblem($"Unknown target '{target}'. Allowed: inapp, generic, flow, midjourney, dalle, flux.");
        }

        var result = await _assetReferenceService.GetCharacterReferencePromptAsync(id, characterId, parsedTarget, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>A billable image-generation call. The result is a candidate: an already approved image is never overwritten.</summary>
    [HttpPost("{id:guid}/characters/{characterId:guid}/reference-image")]
    public async Task<ActionResult<StoryReferenceImageResponse>> GenerateCharacterReferenceImage(Guid id, Guid characterId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _assetReferenceService.GenerateCharacterReferenceAsync(id, characterId, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Generated reference image for StoryCharacter {CharacterId} (Story {StoryId})", characterId, id);
            return Ok(result);
        }
        catch (ReferenceOperationInProgressException ex)
        {
            return ConflictProblem("REFERENCE_OPERATION_IN_PROGRESS", "Reference operation in progress", ex.Message);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
        catch (Exception ex) when (ex is BudgetExceededException or CreditBudgetExceededException)
        {
            _logger.LogWarning(ex, "Reference image generation for StoryCharacter {CharacterId} blocked by the AI budget", characterId);
            return Problem(title: "AI budget exceeded", detail: ex.Message, statusCode: StatusCodes.Status429TooManyRequests);
        }
    }

    /// <summary>Uploads the user's own image as the reference candidate. Free: no AI call, no usage recorded.</summary>
    [HttpPost("{id:guid}/characters/{characterId:guid}/reference-image/upload")]
    [RequestSizeLimit(MaxReferenceUploadRequestBytes)]
    public async Task<ActionResult<StoryReferenceImageResponse>> UploadCharacterReferenceImage(Guid id, Guid characterId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ValidationProblem("Chưa chọn file ảnh.");
        }

        try
        {
            // The client's file name only feeds the extension allow-list; the storage key is generated server-side.
            await using var stream = file.OpenReadStream();
            var result = await _assetReferenceService.UploadCharacterReferenceAsync(id, characterId, file.FileName, stream, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Uploaded reference image candidate for StoryCharacter {CharacterId} (Story {StoryId})", characterId, id);
            return Ok(result);
        }
        catch (ReferenceOperationInProgressException ex)
        {
            return ConflictProblem("REFERENCE_OPERATION_IN_PROGRESS", "Reference operation in progress", ex.Message);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpGet("{id:guid}/characters/{characterId:guid}/reference-image/file")]
    public async Task<IActionResult> GetCharacterReferenceImageFile(Guid id, Guid characterId, CancellationToken cancellationToken)
    {
        var story = await _repository.GetByIdAsync(id, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (character is null || string.IsNullOrWhiteSpace(character.ReferenceImagePath))
        {
            return NotFound();
        }

        return await ServeImageAsync(character.ReferenceImagePath, cancellationToken);
    }

    [HttpGet("{id:guid}/characters/{characterId:guid}/reference-image/pending/file")]
    public async Task<IActionResult> GetCharacterPendingReferenceImageFile(Guid id, Guid characterId, CancellationToken cancellationToken)
    {
        var story = await _repository.GetByIdAsync(id, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (character is null || string.IsNullOrWhiteSpace(character.PendingReferenceImagePath))
        {
            return NotFound();
        }

        return await ServeImageAsync(character.PendingReferenceImagePath, cancellationToken);
    }

    /// <summary>
    /// Approves the character's reference image. When a pending candidate would replace an
    /// already approved image, confirmReplace=true is REQUIRED (else 409 Conflict with
    /// code REFERENCE_REPLACE_CONFIRMATION_REQUIRED) - the server never replaces silently.
    /// The optional expectedPendingVersion (the pendingVersion the client last saw) ties the approve
    /// to that exact candidate: a different current candidate returns 409 with code REFERENCE_PENDING_CHANGED.
    /// Omitted = no such check.
    /// </summary>
    [HttpPut("{id:guid}/characters/{characterId:guid}/reference-image/approve")]
    public async Task<ActionResult<StoryReferenceImageResponse>> ApproveCharacterReferenceImage(Guid id, Guid characterId, [FromQuery] bool confirmReplace, [FromQuery] string? expectedPendingVersion, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _assetReferenceService.ApproveCharacterReferenceAsync(id, characterId, confirmReplace, expectedPendingVersion, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ReferenceReplaceConfirmationRequiredException ex)
        {
            return ConflictProblem("REFERENCE_REPLACE_CONFIRMATION_REQUIRED", "Replace confirmation required", ex.Message);
        }
        catch (ReferencePendingChangedException ex)
        {
            return ConflictProblem("REFERENCE_PENDING_CHANGED", "Pending reference image changed", ex.Message);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    /// <summary>Discards the pending candidate image, keeping the current one. Idempotent.</summary>
    [HttpDelete("{id:guid}/characters/{characterId:guid}/reference-image/pending")]
    public async Task<ActionResult<StoryReferenceImageResponse>> DiscardCharacterPendingReferenceImage(Guid id, Guid characterId, CancellationToken cancellationToken)
    {
        var result = await _assetReferenceService.DiscardPendingCharacterReferenceAsync(id, characterId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/locations/{locationId:guid}/reference-image")]
    public async Task<ActionResult<StoryReferenceImageResponse>> GenerateLocationReferenceImage(Guid id, Guid locationId, CancellationToken cancellationToken)
    {
        var result = await _assetReferenceService.GenerateLocationReferenceAsync(id, locationId, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        _logger.LogInformation("Generated reference image for StoryLocation {LocationId} (Story {StoryId})", locationId, id);
        return Ok(result);
    }

    [HttpGet("{id:guid}/locations/{locationId:guid}/reference-image/file")]
    public async Task<IActionResult> GetLocationReferenceImageFile(Guid id, Guid locationId, CancellationToken cancellationToken)
    {
        var story = await _repository.GetByIdAsync(id, cancellationToken);
        var location = story?.Locations.FirstOrDefault(l => l.Id == locationId);
        if (location is null || string.IsNullOrWhiteSpace(location.ReferenceImagePath))
        {
            return NotFound();
        }

        return await ServeImageAsync(location.ReferenceImagePath, cancellationToken);
    }

    [HttpPut("{id:guid}/locations/{locationId:guid}/reference-image/approve")]
    public async Task<ActionResult<StoryReferenceImageResponse>> ApproveLocationReferenceImage(Guid id, Guid locationId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _assetReferenceService.ApproveLocationReferenceAsync(id, locationId, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    /// <summary>409 with a machine-readable <c>code</c> extension the UI branches on.</summary>
    private ObjectResult ConflictProblem(string code, string title, string detail)
    {
        var problem = new ProblemDetails { Title = title, Detail = detail, Status = StatusCodes.Status409Conflict };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
    }

    private async Task<IActionResult> ServeImageAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!ContentTypeProvider.TryGetContentType(imagePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        var stream = await _fileStorage.GetAsync(imagePath, cancellationToken);
        return File(stream, contentType);
    }

    /// <summary>
    /// "Create Video"/"Open Video" - creates-or-reuses the ContentProject that
    /// generates this episode's video, feeding in the episode's already
    /// written script, then hands off entirely to the existing unchanged
    /// Storyboard/Scene/Asset/TTS/Caption/Render/Publish pipeline from there.
    /// No AI call is made here, so unlike the generation endpoints below this
    /// does not need to catch <see cref="AgentGenerationException"/>.
    /// </summary>
    [HttpPost("{id:guid}/episodes/{episodeId:guid}/video")]
    public async Task<ActionResult<StoryVideoLinkResponse>> CreateOrOpenVideo(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _videoLinkService.CreateOrOpenVideoAsync(id, episodeId, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            if (result.Created)
            {
                _logger.LogInformation(
                    "Created ContentProject {ContentProjectId} for StoryEpisode {EpisodeId} (Story {StoryId})",
                    result.ContentProjectId, episodeId, id);
            }

            return Ok(result);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    /// <summary>
    /// Re-syncs Story-level approved Character/Location reference images onto
    /// an episode's already-linked ContentProject - covers episodes whose
    /// ContentProject was created before the Story-level image(s) existed/
    /// were approved (see <see cref="IStoryVideoLinkService.SyncStoryReferencesAsync"/>).
    /// Purely additive - never overwrites an already-Approved slot. No AI
    /// call is made here (it copies an already-generated, already-approved
    /// image), so unlike the generation endpoints above this does not need
    /// to catch <see cref="AgentGenerationException"/>.
    /// </summary>
    [HttpPost("{id:guid}/episodes/{episodeId:guid}/video/sync-references")]
    public async Task<ActionResult<StorySyncReferencesResponse>> SyncVideoReferences(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _videoLinkService.SyncStoryReferencesAsync(id, episodeId, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            _logger.LogInformation(
                "Synced reference images for StoryEpisode {EpisodeId} (Story {StoryId}): {SyncedLabels}",
                episodeId, id, string.Join(", ", result.SyncedLabels));

            return Ok(result);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
    }

    [HttpGet("{id:guid}/state")]
    public async Task<ActionResult<StoryStateResponse>> GetState(Guid id, CancellationToken cancellationToken)
    {
        var state = await _service.GetStateAsync(id, cancellationToken);
        return state is null ? NotFound() : Ok(state);
    }

    // ---- AI generation (Bible -> Episode Outline -> Script -> Validate -> Finalize) ----
    // Real logic lives in IStoryContinuityManager; these actions stay thin.

    [HttpPost("{id:guid}/bible")]
    public async Task<ActionResult<StoryBibleResponse>> PlanBible(Guid id, [FromBody] PlanStoryBibleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var bible = await _continuityManager.PlanStoryAsync(id, request, cancellationToken);
            if (bible is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Generated Story Bible for Story {StoryId}", id);
            return Ok(bible);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
        catch (AgentGenerationException ex)
        {
            return AiGenerationProblem(ex, "generating the Story Bible", id);
        }
    }

    [HttpGet("{id:guid}/bible")]
    public async Task<ActionResult<StoryBibleResponse>> GetBible(Guid id, CancellationToken cancellationToken)
    {
        var bible = await _continuityManager.GetBibleAsync(id, cancellationToken);
        return bible is null ? NotFound() : Ok(bible);
    }

    [HttpPut("{id:guid}/bible")]
    public async Task<ActionResult<StoryBibleResponse>> UpdateBible(Guid id, [FromBody] UpdateStoryBibleRequest request, CancellationToken cancellationToken)
    {
        var bible = await _continuityManager.UpdateBibleAsync(id, request, cancellationToken);
        return bible is null ? NotFound() : Ok(bible);
    }

    [HttpPost("{id:guid}/episodes/{episodeId:guid}/outline")]
    public async Task<ActionResult<StoryEpisodeOutlineResponse>> PlanEpisodeOutline(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        try
        {
            var outline = await _continuityManager.PlanEpisodeAsync(id, episodeId, cancellationToken);
            if (outline is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Generated Episode Outline for StoryEpisode {EpisodeId} (Story {StoryId})", episodeId, id);
            return Ok(outline);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
        catch (AgentGenerationException ex)
        {
            return AiGenerationProblem(ex, "generating the episode outline", episodeId);
        }
    }

    [HttpGet("{id:guid}/episodes/{episodeId:guid}/outline")]
    public async Task<ActionResult<StoryEpisodeOutlineResponse>> GetEpisodeOutline(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        var outline = await _continuityManager.GetOutlineAsync(id, episodeId, cancellationToken);
        return outline is null ? NotFound() : Ok(outline);
    }

    [HttpPost("{id:guid}/episodes/{episodeId:guid}/script")]
    public async Task<ActionResult<StoryEpisodeResponse>> WriteEpisodeScript(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        try
        {
            var episode = await _continuityManager.WriteScriptAsync(id, episodeId, cancellationToken);
            if (episode is null)
            {
                return NotFound();
            }

            _logger.LogInformation("Wrote script for StoryEpisode {EpisodeId} (Story {StoryId})", episodeId, id);
            return Ok(episode);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
        catch (AgentGenerationException ex)
        {
            return AiGenerationProblem(ex, "writing the episode script", episodeId);
        }
    }

    [HttpPost("{id:guid}/episodes/{episodeId:guid}/validate")]
    public async Task<ActionResult<ContinuityValidationResponse>> ValidateEpisode(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _continuityManager.ValidateEpisodeAsync(id, episodeId, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
        catch (AgentGenerationException ex)
        {
            return AiGenerationProblem(ex, "validating episode continuity", episodeId);
        }
    }

    /// <summary>
    /// Always 200 OK when the Story/Episode exist - both the "isValid: true,
    /// episode completed" and "isValid: false, warnings, nothing mutated"
    /// outcomes are expected, normal responses the client branches on, not
    /// errors. 404/422 are reserved for actual errors (missing ids, missing
    /// script).
    /// </summary>
    [HttpPost("{id:guid}/episodes/{episodeId:guid}/finalize")]
    public async Task<ActionResult<FinalizeEpisodeResponse>> FinalizeEpisode(Guid id, Guid episodeId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _continuityManager.FinalizeEpisodeAsync(id, episodeId, cancellationToken);
            if (result is null)
            {
                return NotFound();
            }

            if (result.Valid)
            {
                _logger.LogInformation("Finalized StoryEpisode {EpisodeId} (Story {StoryId})", episodeId, id);
            }
            else
            {
                _logger.LogInformation(
                    "Finalize blocked by continuity validation for StoryEpisode {EpisodeId} (Story {StoryId}): {CriticalCount} critical issue(s), {WarningCount} warning(s)",
                    episodeId, id, result.CriticalIssues.Count, result.Warnings.Count);
            }

            return Ok(result);
        }
        catch (DomainException ex)
        {
            return ValidationProblem(ex.Message);
        }
        catch (AgentGenerationException ex)
        {
            return AiGenerationProblem(ex, "finalizing the episode", episodeId);
        }
    }

    private ActionResult ValidationProblem(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        return ValidationProblem(ModelState);
    }

    /// <summary>
    /// AgentGenerationException means the LLM call itself failed (invalid/
    /// truncated JSON even after JsonAgentRunner's repair retry, or a
    /// provider-call failure) - a transient upstream failure, not a bug in
    /// the request. 502 Bad Gateway + a clear, retry-suggesting message,
    /// per AgentGenerationException's own doc comment ("callers... should
    /// catch this... rather than letting it bubble as a 500"). Logged as a
    /// warning (expected/retryable), not an error.
    /// </summary>
    private ActionResult AiGenerationProblem(AgentGenerationException ex, string action, Guid relatedId)
    {
        _logger.LogWarning(ex, "AI generation failed while {Action} (id {RelatedId})", action, relatedId);
        return Problem(
            title: "AI generation failed",
            detail: $"The AI provider failed while {action}. This is usually transient - please try again.",
            statusCode: StatusCodes.Status502BadGateway);
    }
}
