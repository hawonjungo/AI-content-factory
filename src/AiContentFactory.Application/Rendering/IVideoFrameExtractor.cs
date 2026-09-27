namespace AiContentFactory.Application.Rendering;

/// <summary>
/// Pulls single still frames out of a stored video (local FFmpeg, no AI, no
/// cost). Used to continue a clip from the previous clip's last frame and to
/// sample frames for the AI clip check.
/// </summary>
public interface IVideoFrameExtractor
{
    /// <summary>
    /// Returns one PNG frame of the video at <paramref name="absoluteVideoPath"/>:
    /// the frame at <paramref name="atSeconds"/>, or the very last frame when it
    /// is null. <paramref name="maxHeight"/> downscales (never upscales) the
    /// frame, keeping its aspect ratio. Throws <see cref="InvalidOperationException"/>
    /// when no frame could be extracted.
    /// </summary>
    Task<byte[]> ExtractFrameAsync(string absoluteVideoPath, double? atSeconds, CancellationToken cancellationToken = default, int? maxHeight = null);
}
