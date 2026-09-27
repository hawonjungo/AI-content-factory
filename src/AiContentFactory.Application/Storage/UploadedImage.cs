using AiContentFactory.Domain.Exceptions;

namespace AiContentFactory.Application.Storage;

/// <summary>
/// Shared first-line checks for a user-uploaded still image: a size cap read
/// without buffering past it, and PNG/JPEG recognised by file signature (the
/// client's file name and content type are never trusted). Same rules as the
/// Story character reference upload.
/// </summary>
public static class UploadedImage
{
    public const int MaxBytes = 10 * 1024 * 1024;

    /// <summary>Reads at most <see cref="MaxBytes"/>; anything larger is rejected without buffering the rest.</summary>
    public static async Task<byte[]> ReadCappedAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new DomainException($"File ảnh quá lớn (tối đa {MaxBytes / (1024 * 1024)} MB).");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new DomainException("File ảnh rỗng.");
        }

        return buffer.ToArray();
    }

    /// <summary>".png" / ".jpg" from the file signature; null for anything else.</summary>
    public static string? SniffExtension(byte[] bytes)
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

    public static string MimeTypeFor(string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
}
