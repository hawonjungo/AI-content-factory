using System.Text;
using AiContentFactory.Application.Agents;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Stories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Stories;

/// <summary>
/// Thrown when approving a pending candidate would replace an already
/// approved Story character reference image and the caller did not confirm.
/// A <see cref="DomainException"/> (so any generic handler still answers 400);
/// the API maps this specific type to 409 Conflict so the UI can ask the user.
/// </summary>
public class ReferenceReplaceConfirmationRequiredException : DomainException
{
    public ReferenceReplaceConfirmationRequiredException()
        : base("Nhân vật này đã có ảnh tham chiếu được duyệt. Duyệt ảnh mới sẽ thay thế ảnh hiện tại cho các tập video tạo sau - hãy xác nhận thay thế (confirmReplace=true) để tiếp tục.")
    {
    }
}

/// <summary>
/// Thrown when the pending candidate the client confirmed is no longer the current one (another
/// tab generated/uploaded a newer candidate, or it was discarded/approved meanwhile). Mapped to
/// 409 with code REFERENCE_PENDING_CHANGED so the UI can reload and ask again.
/// </summary>
public class ReferencePendingChangedException : DomainException
{
    public ReferencePendingChangedException()
        : base("Ảnh chờ duyệt đã thay đổi kể từ lần bạn xem (có thể do một tab khác vừa tạo/tải ảnh mới hoặc bỏ ảnh). Hãy tải lại và xem ảnh hiện tại trước khi duyệt.")
    {
    }
}

/// <summary>
/// Thrown when a generation/upload for the same character is already running (a double click, a second
/// tab, or a direct POST). Mapped to 409 with code REFERENCE_OPERATION_IN_PROGRESS.
/// </summary>
public class ReferenceOperationInProgressException : DomainException
{
    public ReferenceOperationInProgressException()
        : base("Nhân vật này đang có một thao tác tạo/tải ảnh tham chiếu chưa xong. Hãy đợi thao tác đó hoàn tất rồi thử lại.")
    {
    }
}

/// <summary>
/// Generates and approves the single reusable reference image a
/// <see cref="StoryCharacter"/>/<see cref="StoryLocation"/> keeps for its
/// whole Story - generated ONCE and reused across every episode via
/// <see cref="IStoryVideoLinkService"/>'s seeding step, unlike
/// <see cref="AssetReferences.IAssetReferenceGenerationService"/> which
/// generates per-<see cref="ContentProjects.ContentProject"/> variants from a
/// script. The prompt here is composed deterministically from the
/// character/location's own structured fields plus the Story's style preset
/// (look-only wording) - no LLM call, since nothing about a
/// character's own name/description needs to be "written"; only the image
/// generation itself is billable. Kept out of the Continuity/ folder since
/// this isn't an AI-continuity-orchestration operation (no agent calls).
/// A new character image (generated or uploaded) is a CANDIDATE
/// (<see cref="StoryCharacter.SetReferenceCandidate"/>): it never silently
/// replaces an approved image.
/// </summary>
public interface IStoryAssetReferenceService
{
    /// <summary>
    /// Null if the Story or Character doesn't exist (or the character belongs to a different Story).
    /// A billable call: throws <see cref="DomainException"/> (before any provider call) when the character has
    /// neither an appearance (VisualDescription) nor a usable species, and <see cref="ReferenceOperationInProgressException"/>
    /// when a generation/upload for it is already running. The result is a candidate - see the type remarks.
    /// </summary>
    Task<StoryReferenceImageResponse?> GenerateCharacterReferenceAsync(Guid storyId, Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a user-supplied image as the character's reference candidate (source Uploaded). Free: no provider
    /// call and no usage record. Null if the Story/Character doesn't exist. Throws <see cref="DomainException"/>
    /// for a disallowed extension (PNG and JPEG only), an empty/oversized file, a file that is not a readable image
    /// or one over the pixel caps, and <see cref="ReferenceOperationInProgressException"/> while another operation runs.
    /// <paramref name="fileName"/> is only used for the extension allow-list - never for the storage key.
    /// </summary>
    Task<StoryReferenceImageResponse?> UploadCharacterReferenceAsync(Guid storyId, Guid characterId, string? fileName, Stream content, CancellationToken cancellationToken = default);

    /// <summary>No AI call, no cost, and no requirement that the character is generation-ready. Null if the Story/Character doesn't exist.</summary>
    Task<CharacterReferencePromptResponse?> GetCharacterReferencePromptAsync(Guid storyId, Guid characterId, ReferencePromptTarget target, CancellationToken cancellationToken = default);

    /// <summary>Drops the pending candidate (and its file), keeping the current image. Idempotent. Null if the Story/Character doesn't exist.</summary>
    Task<StoryReferenceImageResponse?> DiscardPendingCharacterReferenceAsync(Guid storyId, Guid characterId, CancellationToken cancellationToken = default);

    /// <summary>Null if the Story or Location doesn't exist (or the location belongs to a different Story).</summary>
    Task<StoryReferenceImageResponse?> GenerateLocationReferenceAsync(Guid storyId, Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// With a pending candidate: replaces an already approved image only when <paramref name="confirmReplace"/> is
    /// true (else <see cref="ReferenceReplaceConfirmationRequiredException"/>); with nothing approved yet it approves
    /// the current image. Without a candidate it is the legacy approve. Other <see cref="DomainException"/>s
    /// (propagated from <see cref="StoryCharacter.ApproveReferenceImage"/>) mean there is no generated image yet.
    /// When <paramref name="expectedPendingVersion"/> is supplied and differs from the current
    /// <see cref="ReferencePendingVersion"/>, throws <see cref="ReferencePendingChangedException"/>; when omitted the check is skipped.
    /// </summary>
    Task<StoryReferenceImageResponse?> ApproveCharacterReferenceAsync(Guid storyId, Guid characterId, bool confirmReplace = false, string? expectedPendingVersion = null, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="AiContentFactory.Domain.Exceptions.DomainException"/> (propagated from <see cref="StoryLocation.ApproveReferenceImage"/>) if the image hasn't been generated yet.</summary>
    Task<StoryReferenceImageResponse?> ApproveLocationReferenceAsync(Guid storyId, Guid locationId, CancellationToken cancellationToken = default);
}

public class StoryAssetReferenceService : IStoryAssetReferenceService
{
    /// <summary>Upload size cap (also enforced at the HTTP layer); the service re-checks so it never buffers more than this.</summary>
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    public const string UploadProviderLabel = "user-upload";

    /// <summary>Longest side accepted for an uploaded image.</summary>
    public const int MaxUploadDimensionPx = 8192;

    /// <summary>Largest pixel count (width x height) accepted for an uploaded image (~40 megapixels).</summary>
    public const long MaxUploadPixels = 40_000_000;

    // PNG and JPEG only: downstream (AssetReferenceService.LoadApprovedImagesAsync) labels every non-.png
    // reference as image/jpeg for Gemini/Veo, so a WebP would be sent with the wrong MIME type.
    private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>Upper bound for the ffprobe call on an uploaded file (init-only: DI keeps the default, tests shorten it).</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(15);

    private readonly IStoryRepository _storyRepository;
    private readonly IImageGenerationProvider _imageProvider;
    private readonly IFileStorage _fileStorage;
    private readonly IMediaProbe _mediaProbe;
    private readonly IAiUsageTracker _usageTracker;
    private readonly ICreditLedger _creditLedger;
    private readonly IStoryReferenceGenerationGate _gate;
    private readonly PricingOptions _pricing;
    private readonly CreditCostOptions _creditCosts;
    private readonly ILogger<StoryAssetReferenceService> _logger;

    public StoryAssetReferenceService(
        IStoryRepository storyRepository,
        IImageGenerationProvider imageProvider,
        IFileStorage fileStorage,
        IMediaProbe mediaProbe,
        IAiUsageTracker usageTracker,
        ICreditLedger creditLedger,
        IStoryReferenceGenerationGate gate,
        IOptions<PricingOptions> pricing,
        IOptions<CreditCostOptions> creditCosts,
        ILogger<StoryAssetReferenceService> logger)
    {
        _storyRepository = storyRepository;
        _imageProvider = imageProvider;
        _fileStorage = fileStorage;
        _mediaProbe = mediaProbe;
        _usageTracker = usageTracker;
        _creditLedger = creditLedger;
        _gate = gate;
        _pricing = pricing.Value;
        _creditCosts = creditCosts.Value;
        _logger = logger;
    }

    public async Task<StoryReferenceImageResponse?> GenerateCharacterReferenceAsync(Guid storyId, Guid characterId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (story is null || character is null)
        {
            return null;
        }

        var spec = CharacterReferenceSpec.FromCharacter(character, PresetCatalog.FindStyle(story.StylePresetId));
        if (!spec.HasEnoughToGenerate)
        {
            // A name-only prompt would make the model invent the whole character - a wasted paid call.
            throw new DomainException(
                $"Nhân vật '{character.Name}' chưa có mô tả \"Ngoại hình\" (hoặc loài, với nhân vật không phải người). Hãy điền \"Ngoại hình\" của nhân vật trước khi tạo ảnh tham chiếu để tránh tốn phí tạo ảnh không có căn cứ.");
        }

        // One billable generation/upload per character at a time (double click, second tab, direct POST).
        using var lease = _gate.TryEnter(characterId) ?? throw new ReferenceOperationInProgressException();

        var composed = CharacterReferencePromptComposer.Compose(spec, ReferencePromptTarget.InApp);

        // Same pre-flight guards as SceneKeyframeService's paid image call: monthly USD budget, then the daily credit pool.
        await _usageTracker.EnsureBudgetAvailableAsync(cancellationToken);
        await _creditLedger.EnsureAvailableAsync(_creditCosts.ImageCredits, cancellationToken);

        try
        {
            var image = await _imageProvider.GenerateAsync(new ImageGenerationRequest(composed.Prompt, composed.NegativePrompt, ReferenceImages: null), cancellationToken);
            var extension = image.MimeType.Contains("png") ? "png" : "jpg";
            var storedPath = await _fileStorage.SaveAsync(
                $"stories/{storyId}/characters/{characterId}/reference-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.{extension}",
                image.ImageBytes,
                cancellationToken);

            // The provider call took seconds: the character was loaded BEFORE it, and a tracked entity is not
            // refreshed by a second query. Re-read the persisted state and only then decide (Generated ->
            // pending vs. Approved -> pending), so a concurrent approve is never overwritten by this image.
            await _storyRepository.ReloadCharacterAsync(character, cancellationToken);

            var replacedPendingPath = character.PendingReferenceImagePath;
            character.SetReferenceCandidate(storedPath, composed.Prompt, image.Model, ReferenceImageSource.Generated);
            await _storyRepository.SaveChangesAsync(cancellationToken);

            await _usageTracker.RecordAsync(
                new RecordUsageInput("gemini", image.Model, "story_character_reference_image_generation", _pricing.ImageUsd, ContentProjectId: null, SceneId: null),
                cancellationToken);

            await TryDeleteFileAsync(replacedPendingPath);

            _logger.LogInformation(
                "Generated reference image candidate for StoryCharacter {CharacterId} (Story {StoryId}); pending={HasPending}",
                characterId, storyId, character.HasPendingReferenceImage);
            return StoryReferenceImageResponse.FromCharacter(character);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reference image generation failed for StoryCharacter {CharacterId} (Story {StoryId})", characterId, storyId);
            throw;
        }
    }

    public async Task<StoryReferenceImageResponse?> UploadCharacterReferenceAsync(Guid storyId, Guid characterId, string? fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (story is null || character is null)
        {
            return null;
        }

        // The client's file name is only ever inspected for its extension.
        var clientExtension = string.IsNullOrWhiteSpace(fileName) ? string.Empty : Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(clientExtension))
        {
            throw new DomainException($"Định dạng ảnh không hỗ trợ '{clientExtension}'. Cho phép: {string.Join(", ", AllowedImageExtensions)}.");
        }

        using var lease = _gate.TryEnter(characterId) ?? throw new ReferenceOperationInProgressException();

        var bytes = await ReadCappedAsync(content, cancellationToken);
        if (bytes.Length == 0)
        {
            throw new DomainException("File ảnh rỗng.");
        }

        // The stored extension follows the real content, not the client's claim.
        var storedExtension = SniffImageExtension(bytes)
            ?? throw new DomainException("File không phải ảnh PNG hoặc JPEG hợp lệ.");

        var storedPath = await _fileStorage.SaveAsync(
            $"stories/{storyId}/characters/{characterId}/reference-upload-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{storedExtension}",
            bytes,
            cancellationToken);

        try
        {
            await ValidateStoredImageAsync(storedPath, cancellationToken);
        }
        catch
        {
            await TryDeleteFileAsync(storedPath);
            throw;
        }

        // Same stale-entity rule as generation: decide on the CURRENT persisted state, not the one loaded before the slow work.
        await _storyRepository.ReloadCharacterAsync(character, cancellationToken);

        var replacedPendingPath = character.PendingReferenceImagePath;
        character.SetReferenceCandidate(storedPath, prompt: null, UploadProviderLabel, ReferenceImageSource.Uploaded);
        await _storyRepository.SaveChangesAsync(cancellationToken);
        await TryDeleteFileAsync(replacedPendingPath);

        // Deliberately no usage/credit record: nothing billable happened.
        _logger.LogInformation(
            "Uploaded reference image candidate for StoryCharacter {CharacterId} (Story {StoryId}); pending={HasPending}",
            characterId, storyId, character.HasPendingReferenceImage);
        return StoryReferenceImageResponse.FromCharacter(character);
    }

    public async Task<CharacterReferencePromptResponse?> GetCharacterReferencePromptAsync(Guid storyId, Guid characterId, ReferencePromptTarget target, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (story is null || character is null)
        {
            return null;
        }

        var spec = CharacterReferenceSpec.FromCharacter(character, PresetCatalog.FindStyle(story.StylePresetId));
        var composed = CharacterReferencePromptComposer.Compose(spec, target);

        return new CharacterReferencePromptResponse(
            CharacterReferencePromptComposer.TargetId(target),
            composed.Prompt,
            composed.NegativePrompt,
            composed.MissingFields,
            composed.Notes);
    }

    public async Task<StoryReferenceImageResponse?> DiscardPendingCharacterReferenceAsync(Guid storyId, Guid characterId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (story is null || character is null)
        {
            return null;
        }

        var pendingPath = character.PendingReferenceImagePath;
        if (pendingPath is not null)
        {
            character.DiscardPendingReferenceImage();
            await _storyRepository.SaveChangesAsync(cancellationToken);
            await TryDeleteFileAsync(pendingPath);
        }

        return StoryReferenceImageResponse.FromCharacter(character);
    }

    public async Task<StoryReferenceImageResponse?> GenerateLocationReferenceAsync(Guid storyId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var location = story?.Locations.FirstOrDefault(l => l.Id == locationId);
        if (story is null || location is null)
        {
            return null;
        }

        var (prompt, negativePrompt) = BuildLocationPrompt(location, story);

        try
        {
            var image = await _imageProvider.GenerateAsync(new ImageGenerationRequest(prompt, negativePrompt, ReferenceImages: null), cancellationToken);
            var extension = image.MimeType.Contains("png") ? "png" : "jpg";
            var storedPath = await _fileStorage.SaveAsync(
                $"stories/{storyId}/locations/{locationId}/reference-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{extension}",
                image.ImageBytes,
                cancellationToken);

            location.MarkReferenceImageGenerated(storedPath, prompt, image.Model);
            await _storyRepository.SaveChangesAsync(cancellationToken);

            await _usageTracker.RecordAsync(
                new RecordUsageInput("gemini", image.Model, "story_location_reference_image_generation", _pricing.ImageUsd, ContentProjectId: null, SceneId: null),
                cancellationToken);

            _logger.LogInformation("Generated reference image for StoryLocation {LocationId} (Story {StoryId})", locationId, storyId);
            return StoryReferenceImageResponse.FromLocation(location);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reference image generation failed for StoryLocation {LocationId} (Story {StoryId})", locationId, storyId);
            throw;
        }
    }

    public async Task<StoryReferenceImageResponse?> ApproveCharacterReferenceAsync(Guid storyId, Guid characterId, bool confirmReplace = false, string? expectedPendingVersion = null, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var character = story?.Characters.FirstOrDefault(c => c.Id == characterId);
        if (story is null || character is null)
        {
            return null;
        }

        // Tie the approve to the candidate the user actually looked at (a confirm shown for candidate A must not promote B).
        if (!string.IsNullOrWhiteSpace(expectedPendingVersion) &&
            !string.Equals(expectedPendingVersion.Trim(), ReferencePendingVersion.Of(character), StringComparison.OrdinalIgnoreCase))
        {
            throw new ReferencePendingChangedException();
        }

        if (character.HasPendingReferenceImage)
        {
            if (character.ReferenceImageStatus == AssetReferenceStatus.Approved)
            {
                // Server-enforced "no silent replace": the client must have asked the user first.
                if (!confirmReplace)
                {
                    throw new ReferenceReplaceConfirmationRequiredException();
                }

                character.PromotePendingReferenceImage();
                _logger.LogInformation(
                    "Replaced approved reference image of StoryCharacter {CharacterId} (Story {StoryId}) with the confirmed candidate",
                    characterId, storyId);
            }
            else
            {
                // Nothing approved yet: approve the current (main) image as before.
                character.ApproveReferenceImage();
            }
        }
        else
        {
            character.ApproveReferenceImage();
        }

        await _storyRepository.SaveChangesAsync(cancellationToken);

        // A replaced approved image's file is deliberately NOT deleted: episode projects seeded earlier still point at it.
        return StoryReferenceImageResponse.FromCharacter(character);
    }

    public async Task<StoryReferenceImageResponse?> ApproveLocationReferenceAsync(Guid storyId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var story = await _storyRepository.GetByIdAsync(storyId, cancellationToken);
        var location = story?.Locations.FirstOrDefault(l => l.Id == locationId);
        if (story is null || location is null)
        {
            return null;
        }

        location.ApproveReferenceImage();
        await _storyRepository.SaveChangesAsync(cancellationToken);

        return StoryReferenceImageResponse.FromLocation(location);
    }

    /// <summary>
    /// Deterministic empty establishing plate of the location - no people or
    /// characters at all, built only from the location's own fields plus the
    /// Story style's look-only wording (no style line at all when the Story has
    /// no style preset - the Bible's tone is not used as a fallback). Story-level
    /// location images are no longer used for episodes (endpoints and columns
    /// are kept for compatibility), but a regenerated one must still never
    /// contain a figure that would leak into a clip, so Bible rules (which
    /// routinely name characters) are not appended here either.
    /// </summary>
    private static (string Prompt, string NegativePrompt) BuildLocationPrompt(StoryLocation location, Story story)
    {
        var style = PresetCatalog.FindStyle(story.StylePresetId);

        var sb = new StringBuilder();
        sb.Append(location.Name).Append(", a recurring location for a short-form video series. ");
        sb.Append(location.VisualDescription ?? location.Description ?? location.Name).Append('.');
        sb.Append(" Empty establishing plate: a wide view of the empty location only. NO people, no characters, no figures,")
          .Append(" no silhouettes, no animals as characters - nothing in the foreground; the place itself is the only subject.");
        AppendStyleLook(sb, style);
        sb.Append(" Empty scene with nobody in it.");

        return (
            sb.ToString(),
            AssetReferencePromptAgent.MergeWithQualityNegative(AssetReferenceType.Environment, style?.ReferenceNegativePrompt));
    }

    /// <summary>
    /// The Story's own style preset, via its LOOK-ONLY reference wording
    /// (<see cref="StylePreset.ReferenceLookGuidance"/> - art style, medium,
    /// materials and color grade, never pose/expression/lighting/framing/
    /// setting). Only a preset that really exists in the catalog counts -
    /// <see cref="PresetCatalog.ResolveStyle"/> would silently substitute the
    /// dark house style for a story that never chose one. A missing/unknown id
    /// adds no style line at all.
    /// </summary>
    private static void AppendStyleLook(StringBuilder sb, StylePreset? style)
    {
        if (style is not null)
        {
            sb.Append(" Rendering style (look only): ").Append(style.ReferenceLookGuidance.Trim().TrimEnd('.')).Append('.');
        }
    }

    /// <summary>Reads at most <see cref="MaxUploadBytes"/>; anything larger is rejected without buffering the rest.</summary>
    private static async Task<byte[]> ReadCappedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxUploadBytes)
            {
                throw new DomainException($"File ảnh quá lớn (tối đa {MaxUploadBytes / (1024 * 1024)} MB).");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Recognises PNG and JPEG by their file signature; null for anything else (WebP included - see the allow-list).</summary>
    private static string? SniffImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return ".png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        return null;
    }

    /// <summary>
    /// Probes the just-stored upload (bounded by <see cref="ProbeTimeout"/>) and enforces the pixel caps. Any failure
    /// is a generic Vietnamese message: the probe's own error text (which can carry filesystem paths) is only logged.
    /// </summary>
    private async Task ValidateStoredImageAsync(string storedPath, CancellationToken cancellationToken)
    {
        MediaInfo media;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(ProbeTimeout);
            try
            {
                media = await _mediaProbe.ProbeAsync(_fileStorage.GetAbsolutePath(storedPath), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Probing an uploaded reference image timed out after {Timeout}", ProbeTimeout);
                throw new DomainException("Không đọc được file ảnh (quá thời gian xử lý). Hãy thử một file ảnh khác.");
            }
        }

        if (!media.Ok || !media.HasVideo || media.Width <= 0 || media.Height <= 0)
        {
            _logger.LogWarning("Uploaded reference image is not a readable image: {ProbeError}", media.Error);
            throw new DomainException("Không đọc được file ảnh. Hãy thử một file PNG hoặc JPEG khác.");
        }

        if (media.Width > MaxUploadDimensionPx || media.Height > MaxUploadDimensionPx || (long)media.Width * media.Height > MaxUploadPixels)
        {
            _logger.LogWarning("Uploaded reference image is too large: {Width}x{Height}", media.Width, media.Height);
            throw new DomainException($"Ảnh quá lớn về kích thước (tối đa {MaxUploadDimensionPx}px mỗi cạnh và khoảng {MaxUploadPixels / 1_000_000} megapixel). Hãy thu nhỏ ảnh rồi tải lại.");
        }
    }

    /// <summary>
    /// Best-effort removal of an orphaned CANDIDATE file (a replaced or discarded pending image, or a
    /// rejected upload). Never throws - a leftover file is harmless, a failed request over cleanup is not.
    /// Never deletes a path that is still referenced by any persisted row (a Story character's current or
    /// pending image, or an episode AssetReference sharing the key); if that check itself fails, nothing is deleted.
    /// </summary>
    private async Task TryDeleteFileAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (await _storyRepository.IsStoredImagePathReferencedAsync(path))
            {
                _logger.LogInformation("Keeping stored reference image {Path}: it is still referenced", path);
                return;
            }

            await _fileStorage.DeleteAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete orphaned reference candidate file {Path}", path);
        }
    }
}
