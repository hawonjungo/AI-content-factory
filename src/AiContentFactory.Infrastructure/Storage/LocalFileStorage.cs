using AiContentFactory.Application.Storage;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly LocalFileStorageOptions _options;

    public LocalFileStorage(IOptions<LocalFileStorageOptions> options)
    {
        _options = options.Value;
    }

    public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(relativePath);
        await File.WriteAllBytesAsync(absolutePath, content, cancellationToken);
        return relativePath;
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(relativePath);

        await using var destination = File.Create(absolutePath);
        await content.CopyToAsync(destination, cancellationToken);

        return relativePath;
    }

    public Task<Stream> GetAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        Stream stream = File.OpenRead(GetAbsolutePath(storedPath));
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(storedPath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
        return Task.CompletedTask;
    }

    public string GetAbsolutePath(string storedPath)
    {
        // Normalize to avoid path traversal outside RootPath and handle
        // either '/' or '\' separators consistently across OSes.
        var segments = storedPath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        var absolutePath = Path.Combine(new[] { _options.RootPath }.Concat(segments).ToArray());

        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return absolutePath;
    }
}
