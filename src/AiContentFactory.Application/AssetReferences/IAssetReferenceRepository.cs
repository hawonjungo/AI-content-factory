using AiContentFactory.Domain.AssetReferences;

namespace AiContentFactory.Application.AssetReferences;

public interface IAssetReferenceRepository
{
    /// <summary>All rows for a project, untracked - callers only read them for display / to load bytes.</summary>
    Task<IReadOnlyList<AssetReference>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default);

    Task<AssetReference?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<AssetReference> references, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically: retire every other Generated variant of this type, then
    /// mark this row Approved. Done with ExecuteUpdate (direct SQL, no tracked
    /// round-trip) so rapid double-clicks or an approve+skip race can't raise
    /// DbUpdateConcurrencyException. Returns rows affected by the approve
    /// (0 = the row is gone / already changed).
    /// </summary>
    Task<int> ApproveAtomicAsync(Guid contentProjectId, Guid refId, AssetReferenceType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically: retire every current row for this type, then insert one
    /// fresh Skipped row. Idempotent.
    /// </summary>
    Task SkipAtomicAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default);

    /// <summary>Retire every Generated/Superseded/Pending row for a type (before generating fresh variants).</summary>
    Task ClearVariantsAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid refId, CancellationToken cancellationToken = default);
}
