namespace AiContentFactory.Infrastructure.Storage;

public class LocalFileStorageOptions
{
    public const string SectionName = "Storage:Local";

    /// <summary>Root directory assets are written under. In Docker this should be a mounted volume so generated files survive container restarts.</summary>
    public string RootPath { get; set; } = "/app/storage";
}
