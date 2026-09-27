using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Application.AssetReferences;

public interface IAssetReferenceService
{
    Task<AssetReferenceSlotsDto> GetSlotsAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The FULL list of reference rows for a project - every (Type, Label)
    /// row that exists, including legacy null-Label rows - unlike
    /// <see cref="GetSlotsAsync"/>'s fixed Character+Environment pair. Additive
    /// read-only capability for the Story-linked multi-named-reference flow;
    /// does not change <see cref="GetSlotsAsync"/>'s own behavior.
    /// </summary>
    Task<IReadOnlyList<NamedAssetReferenceDto>> GetNamedReferencesAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task<AssetReferenceResponse> UploadAsync(Guid contentProjectId, AssetReferenceType type, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<AssetReferenceSlotsDto> ApproveAsync(Guid contentProjectId, Guid refId, CancellationToken cancellationToken = default);

    Task<AssetReferenceSlotsDto> SkipAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default);

    Task DeleteVariantAsync(Guid contentProjectId, Guid refId, CancellationToken cancellationToken = default);

    /// <summary>Both Character and Environment have an Approved or Skipped row - the wizard gate.</summary>
    Task<bool> AreBothResolvedAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approved reference images with bytes loaded, for the generation
    /// pipeline. Each <see cref="ReferenceImage.Label"/> is the row's real
    /// <see cref="AssetReference.Label"/> when set (a Story-linked named
    /// reference, e.g. "Milo"), or falls back to the classic
    /// <see cref="AssetReference.Type"/> string ("Character"/"Environment")
    /// for legacy null-Label rows - unchanged for every existing project.
    /// </summary>
    Task<IReadOnlyList<ReferenceImage>> LoadApprovedImagesAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
}

public class AssetReferenceService : IAssetReferenceService
{
    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    private readonly IAssetReferenceRepository _repository;
    private readonly IFileStorage _fileStorage;

    public AssetReferenceService(IAssetReferenceRepository repository, IFileStorage fileStorage)
    {
        _repository = repository;
        _fileStorage = fileStorage;
    }

    public async Task<AssetReferenceSlotsDto> GetSlotsAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetByProjectAsync(contentProjectId, cancellationToken);
        return new AssetReferenceSlotsDto(
            BuildSlot(contentProjectId, AssetReferenceType.Character, rows),
            BuildSlot(contentProjectId, AssetReferenceType.Environment, rows));
    }

    public async Task<IReadOnlyList<NamedAssetReferenceDto>> GetNamedReferencesAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetByProjectAsync(contentProjectId, cancellationToken);
        return rows
            .OrderBy(r => r.Type)
            .ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.CreatedAt)
            .Select(r => new NamedAssetReferenceDto(
                r.Id,
                r.Type.ToString(),
                r.Label,
                r.Status.ToString(),
                r.ImagePath is null ? null : FileUrl(contentProjectId, r.Id),
                r.Prompt))
            .ToList();
    }

    public async Task<AssetReferenceResponse> UploadAsync(Guid contentProjectId, AssetReferenceType type, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            throw new DomainException($"Định dạng ảnh không hỗ trợ '{extension}'. Cho phép: {string.Join(", ", AllowedImageExtensions)}.");
        }

        var storedPath = await _fileStorage.SaveAsync(
            $"content-projects/{contentProjectId}/asset-references/{type.ToString().ToLowerInvariant()}-upload-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}{extension}",
            content,
            cancellationToken);

        var reference = AssetReference.CreateGenerated(contentProjectId, type, storedPath, prompt: null, provider: "upload");
        await _repository.AddRangeAsync(new[] { reference }, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return AssetReferenceResponse.FromDomain(reference, id => FileUrl(contentProjectId, id));
    }

    public async Task<AssetReferenceSlotsDto> ApproveAsync(Guid contentProjectId, Guid refId, CancellationToken cancellationToken = default)
    {
        var reference = await _repository.GetByIdAsync(refId, cancellationToken);
        if (reference is null || reference.ContentProjectId != contentProjectId)
        {
            throw new DomainException("Ảnh mẫu không tồn tại trên dự án này.");
        }
        if (reference.ImagePath is null)
        {
            throw new DomainException("Ảnh mẫu này chưa có hình để chốt.");
        }

        var affected = await _repository.ApproveAtomicAsync(contentProjectId, refId, reference.Type, cancellationToken);
        if (affected == 0)
        {
            throw new DomainException("Ảnh mẫu đã bị thay đổi, hãy tải lại.");
        }

        return await GetSlotsAsync(contentProjectId, cancellationToken);
    }

    public async Task<AssetReferenceSlotsDto> SkipAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default)
    {
        await _repository.SkipAtomicAsync(contentProjectId, type, cancellationToken);
        return await GetSlotsAsync(contentProjectId, cancellationToken);
    }

    public async Task DeleteVariantAsync(Guid contentProjectId, Guid refId, CancellationToken cancellationToken = default)
    {
        var reference = await _repository.GetByIdAsync(refId, cancellationToken);
        if (reference is null || reference.ContentProjectId != contentProjectId)
        {
            throw new DomainException("Ảnh mẫu không tồn tại trên dự án này.");
        }
        await _repository.DeleteAsync(refId, cancellationToken);
    }

    public async Task<bool> AreBothResolvedAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetByProjectAsync(contentProjectId, cancellationToken);
        return IsResolved(rows, AssetReferenceType.Character) && IsResolved(rows, AssetReferenceType.Environment);
    }

    public async Task<IReadOnlyList<ReferenceImage>> LoadApprovedImagesAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetByProjectAsync(contentProjectId, cancellationToken);
        var approved = rows
            .Where(r => r.Status == AssetReferenceStatus.Approved && !string.IsNullOrWhiteSpace(r.ImagePath))
            .OrderBy(r => r.Type);

        var images = new List<ReferenceImage>();
        foreach (var row in approved)
        {
            await using var stream = await _fileStorage.GetAsync(row.ImagePath!, cancellationToken);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            var mime = row.ImagePath!.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            // Real Label when this row has one (a Story-linked named
            // reference); otherwise fall back to the classic Type string -
            // exactly the label every legacy row produced before Label existed.
            images.Add(new ReferenceImage(memory.ToArray(), mime, row.Label ?? row.Type.ToString(), row.Prompt));
        }

        return images;
    }

    private AssetReferenceSlotDto BuildSlot(Guid contentProjectId, AssetReferenceType type, IReadOnlyList<AssetReference> rows)
    {
        var ofType = rows.Where(r => r.Type == type).ToList();

        var resolved = ofType.FirstOrDefault(r => r.Status is AssetReferenceStatus.Approved or AssetReferenceStatus.Skipped);
        var variants = ofType
            .Where(r => r.Status == AssetReferenceStatus.Generated && r.ImagePath is not null)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new AssetReferenceVariantDto(r.Id, FileUrl(contentProjectId, r.Id)))
            .ToList();

        var status = resolved?.Status.ToString()
            ?? (variants.Count > 0 ? nameof(AssetReferenceStatus.Generated) : nameof(AssetReferenceStatus.Pending));

        return new AssetReferenceSlotDto(
            type.ToString(),
            status,
            resolved?.ImagePath is null ? null : FileUrl(contentProjectId, resolved.Id),
            resolved?.Prompt ?? ofType.OrderByDescending(r => r.CreatedAt).FirstOrDefault()?.Prompt,
            variants);
    }

    private static bool IsResolved(IReadOnlyList<AssetReference> rows, AssetReferenceType type) =>
        rows.Any(r => r.Type == type && r.Status is AssetReferenceStatus.Approved or AssetReferenceStatus.Skipped);

    private static string FileUrl(Guid contentProjectId, Guid refId) =>
        $"/content-projects/{contentProjectId}/asset-references/{refId}/file";
}
