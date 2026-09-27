using AiContentFactory.Application.AssetReferences;
using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Tests.Fakes;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers <see cref="AssetReferenceService.GetNamedReferencesAsync"/>: the new
/// additive "full list of named reference rows" read, added alongside (and
/// without changing) <see cref="AssetReferenceService.GetSlotsAsync"/>'s
/// existing fixed Character+Environment pair.
/// </summary>
public class AssetReferenceServiceTests
{
    private static (AssetReferenceService Service, FakeAssetReferenceRepository Repo, FakeFileStorage Storage) Build()
    {
        var repo = new FakeAssetReferenceRepository();
        var storage = new FakeFileStorage();
        var service = new AssetReferenceService(repo, storage);
        return (service, repo, storage);
    }

    [Fact]
    public async Task GetNamedReferencesAsync_returns_every_row_including_legacy_null_label_rows()
    {
        var (service, repo, _) = Build();
        var projectId = Guid.NewGuid();

        var milo = AssetReference.CreateGenerated(projectId, AssetReferenceType.Character, "refs/milo.png", "milo prompt", "gemini", label: "Milo");
        milo.Approve();
        var mimi = AssetReference.CreateGenerated(projectId, AssetReferenceType.Character, "refs/mimi.png", "mimi prompt", "gemini", label: "Mimi");
        mimi.Approve();
        var legacyEnvironment = AssetReference.CreateGenerated(projectId, AssetReferenceType.Environment, "refs/legacy-env.png", "legacy prompt", "gemini");
        legacyEnvironment.Approve();

        repo.Rows.AddRange(new[] { milo, mimi, legacyEnvironment });

        // A row on a different project must not leak in.
        var otherProjectRow = AssetReference.CreateGenerated(Guid.NewGuid(), AssetReferenceType.Character, "refs/other.png", null, "gemini", label: "Someone Else");
        repo.Rows.Add(otherProjectRow);

        var result = await service.GetNamedReferencesAsync(projectId);

        Assert.Equal(3, result.Count);

        var miloDto = Assert.Single(result, r => r.Label == "Milo");
        Assert.Equal("Character", miloDto.Type);
        Assert.Equal("Approved", miloDto.Status);
        Assert.NotNull(miloDto.ImageUrl);
        Assert.Equal("milo prompt", miloDto.Prompt);

        var mimiDto = Assert.Single(result, r => r.Label == "Mimi");
        Assert.Equal("Character", mimiDto.Type);

        var legacyDto = Assert.Single(result, r => r.Type == "Environment");
        Assert.Null(legacyDto.Label);
        Assert.Equal("Approved", legacyDto.Status);
    }

    [Fact]
    public async Task GetNamedReferencesAsync_returns_empty_list_for_a_project_with_no_references()
    {
        var (service, _, _) = Build();

        var result = await service.GetNamedReferencesAsync(Guid.NewGuid());

        Assert.Empty(result);
    }

    /// <summary>
    /// Regression test for the Label pass-through fix: before this fix,
    /// <see cref="AssetReferenceService.LoadApprovedImagesAsync"/> always set
    /// <see cref="AiContentFactory.Application.Providers.ReferenceImage.Label"/>
    /// to the row's <see cref="AssetReference.Type"/> string, discarding a
    /// named row's real <see cref="AssetReference.Label"/> ("Milo") entirely.
    /// </summary>
    [Fact]
    public async Task LoadApprovedImagesAsync_uses_the_row_real_label_not_a_hardcoded_type_string()
    {
        var (service, repo, storage) = Build();
        var projectId = Guid.NewGuid();

        var milo = AssetReference.CreateGenerated(projectId, AssetReferenceType.Character, "refs/milo.png", "milo prompt", "gemini", label: "Milo");
        milo.Approve();
        repo.Rows.Add(milo);
        await storage.SaveAsync("refs/milo.png", new byte[] { 1, 2, 3 });

        var images = await service.LoadApprovedImagesAsync(projectId);

        var image = Assert.Single(images);
        Assert.Equal("Milo", image.Label);
    }

    /// <summary>
    /// Legacy null-Label rows must keep producing the classic "Character"/
    /// "Environment" label - unchanged for every project that predates the
    /// multi-named-reference feature.
    /// </summary>
    [Fact]
    public async Task LoadApprovedImagesAsync_falls_back_to_the_type_string_for_a_legacy_null_label_row()
    {
        var (service, repo, storage) = Build();
        var projectId = Guid.NewGuid();

        var legacy = AssetReference.CreateGenerated(projectId, AssetReferenceType.Environment, "refs/env.png", "legacy prompt", "gemini");
        legacy.Approve();
        repo.Rows.Add(legacy);
        await storage.SaveAsync("refs/env.png", new byte[] { 4, 5, 6 });

        var images = await service.LoadApprovedImagesAsync(projectId);

        var image = Assert.Single(images);
        Assert.Equal("Environment", image.Label);
    }
}
