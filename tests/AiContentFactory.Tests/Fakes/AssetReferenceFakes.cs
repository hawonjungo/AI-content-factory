using System.Text;
using AiContentFactory.Application.Agents;
using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Storage;
using AiContentFactory.Domain.AssetReferences;

namespace AiContentFactory.Tests.Fakes;

/// <summary>Records what it was asked for and returns a controllable (or sensible default) prompt - so tests can assert the service passes real script/style/idea-config context through, without making a real LLM call.</summary>
public sealed class FakeAssetReferencePromptAgent : IAssetReferencePromptAgent
{
    public AssetReferencePromptAgentInput? LastInput { get; private set; }
    public int Calls { get; private set; }
    public Func<AssetReferencePromptAgentInput, AssetReferencePromptOutput>? Respond { get; set; }

    public Task<AssetReferencePromptOutput> GenerateAsync(AssetReferencePromptAgentInput input, CancellationToken cancellationToken = default)
    {
        LastInput = input;
        Calls++;
        return Task.FromResult(Respond?.Invoke(input) ?? DefaultOutput(input));
    }

    private static AssetReferencePromptOutput DefaultOutput(AssetReferencePromptAgentInput input) =>
        new(
            new AssetReferenceVisualAnalysis(
                MainSubjects: new[] { "test subject" },
                Action: "test action",
                Environment: "test environment",
                Emotion: "test emotion",
                Tone: "test tone",
                VisualStyle: input.StyleGuidance,
                Lighting: "test lighting",
                ColorPalette: "test palette",
                Composition: "test composition"),
            Prompt: $"Generated {input.AssetType} prompt for \"{input.Title}\".",
            NegativePrompt: "text, watermark");
}

public sealed class FakeAssetReferenceRepository : IAssetReferenceRepository
{
    public List<AssetReference> Rows { get; } = new();
    public int ClearVariantsCalls { get; private set; }

    public Task<IReadOnlyList<AssetReference>> GetByProjectAsync(Guid contentProjectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AssetReference>>(Rows.Where(r => r.ContentProjectId == contentProjectId).ToList());

    public Task<AssetReference?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));

    public Task AddRangeAsync(IEnumerable<AssetReference> references, CancellationToken cancellationToken = default)
    {
        Rows.AddRange(references);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> ApproveAtomicAsync(Guid contentProjectId, Guid refId, AssetReferenceType type, CancellationToken cancellationToken = default) =>
        Task.FromResult(1);

    public Task SkipAtomicAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClearVariantsAsync(Guid contentProjectId, AssetReferenceType type, CancellationToken cancellationToken = default)
    {
        ClearVariantsCalls++;
        Rows.RemoveAll(r => r.ContentProjectId == contentProjectId && r.Type == type && r.Status == AssetReferenceStatus.Generated);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid refId, CancellationToken cancellationToken = default)
    {
        Rows.RemoveAll(r => r.Id == refId);
        return Task.CompletedTask;
    }
}

public sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken = default)
    {
        Files[relativePath] = content;
        return Task.FromResult(relativePath);
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        Files[relativePath] = ms.ToArray();
        return relativePath;
    }

    public Task<Stream> GetAsync(string storedPath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(Files.TryGetValue(storedPath, out var b) ? b : Array.Empty<byte>()));

    public Task DeleteAsync(string storedPath, CancellationToken cancellationToken = default)
    {
        Files.Remove(storedPath);
        return Task.CompletedTask;
    }

    public string GetAbsolutePath(string storedPath) => storedPath;
}

public sealed class FakeAiUsageTracker : IAiUsageTracker
{
    public List<RecordUsageInput> Records { get; } = new();

    public Task RecordAsync(RecordUsageInput input, CancellationToken cancellationToken = default)
    {
        Records.Add(input);
        return Task.CompletedTask;
    }

    public Task<decimal> GetCurrentMonthSpendAsync(CancellationToken cancellationToken = default) => Task.FromResult(0m);

    public Task EnsureBudgetAvailableAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal static class PngBytes
{
    // 1x1 transparent PNG - enough for the storage/round-trip path in tests.
    public static byte[] OnePixel() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);
}
