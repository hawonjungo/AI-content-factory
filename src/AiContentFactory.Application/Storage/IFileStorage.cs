namespace AiContentFactory.Application.Storage;

/// <summary>
/// Application-managed asset storage. Provider-hosted URLs (a Veo download
/// link, a Gemini inline blob) are never assumed permanent - callers copy
/// bytes in here immediately, and everything downstream (rendering,
/// publishing) reads from here, never from the original provider.
/// Phase 1 ships a local-filesystem implementation only; S3/R2/Blob
/// implementations can be added later behind this same interface.
/// </summary>
public interface IFileStorage
{
    /// <param name="relativePath">
    /// A path-like key, e.g. "content-projects/{id}/scenes/{sceneId}/voice.wav".
    /// Implementations decide how this maps to actual storage.
    /// </param>
    /// <returns>The stored path/key - callers should persist this verbatim (e.g. on Asset.FilePath) and pass it back to GetAsync later.</returns>
    Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream overload for content that arrives as a stream and may be large -
    /// user-uploaded background music, for instance. Avoids materialising the
    /// whole file in memory the way the byte[] overload does.
    /// </summary>
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> GetAsync(string storedPath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storedPath, CancellationToken cancellationToken = default);

    /// <summary>Absolute filesystem path for a stored key - used by the FFmpeg renderer, which needs real file paths, not streams.</summary>
    string GetAbsolutePath(string storedPath);
}
