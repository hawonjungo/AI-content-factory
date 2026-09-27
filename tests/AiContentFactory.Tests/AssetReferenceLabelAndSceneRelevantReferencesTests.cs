using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Storyboards;
using Xunit;

namespace AiContentFactory.Tests;

/// <summary>
/// Covers the additive <see cref="AssetReference.Label"/> field (multiple
/// named references per project) and the additive
/// <see cref="Scene.RelevantReferenceLabels"/> field (which named references
/// a scene needs), added as the database step of the multi-reference feature.
/// </summary>
public class AssetReferenceLabelAndSceneRelevantReferencesTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();

    [Fact]
    public void CreatePending_without_label_defaults_to_null_label()
    {
        var reference = AssetReference.CreatePending(ProjectId, AssetReferenceType.Character, "a hero", "gemini");

        Assert.Null(reference.Label);
    }

    [Fact]
    public void CreateGenerated_without_label_defaults_to_null_label()
    {
        var reference = AssetReference.CreateGenerated(ProjectId, AssetReferenceType.Character, "refs/char.png", "a hero", "gemini");

        Assert.Null(reference.Label);
    }

    [Fact]
    public void CreateSkipped_without_label_defaults_to_null_label()
    {
        var reference = AssetReference.CreateSkipped(ProjectId, AssetReferenceType.Environment);

        Assert.Null(reference.Label);
    }

    [Fact]
    public void CreateGenerated_with_label_sets_label()
    {
        var reference = AssetReference.CreateGenerated(ProjectId, AssetReferenceType.Character, "refs/milo.png", "a small cat", "gemini", label: "Milo");

        Assert.Equal("Milo", reference.Label);
    }

    [Fact]
    public void CreatePending_with_label_sets_label()
    {
        var reference = AssetReference.CreatePending(ProjectId, AssetReferenceType.Character, "a small cat", "gemini", label: "Milo");

        Assert.Equal("Milo", reference.Label);
    }

    [Fact]
    public void CreateSkipped_with_label_sets_label()
    {
        var reference = AssetReference.CreateSkipped(ProjectId, AssetReferenceType.Environment, label: "Rooftop Garden");

        Assert.Equal("Rooftop Garden", reference.Label);
    }

    [Fact]
    public void Label_is_trimmed_and_blank_is_normalized_to_null()
    {
        var withWhitespace = AssetReference.CreateGenerated(ProjectId, AssetReferenceType.Character, "refs/milo.png", null, "gemini", label: "  Milo  ");
        var blank = AssetReference.CreateGenerated(ProjectId, AssetReferenceType.Character, "refs/x.png", null, "gemini", label: "   ");

        Assert.Equal("Milo", withWhitespace.Label);
        Assert.Null(blank.Label);
    }

    [Fact]
    public void Scene_default_state_has_empty_relevant_reference_labels()
    {
        var storyboard = Storyboard.Create(Guid.NewGuid());
        var scene = storyboard.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        Assert.Empty(scene.RelevantReferenceLabels);
        Assert.Null(scene.RelevantReferenceLabelsText);
    }

    [Fact]
    public void SetRelevantReferenceLabels_preserves_order_and_drops_blanks()
    {
        var storyboard = Storyboard.Create(Guid.NewGuid());
        var scene = storyboard.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        scene.SetRelevantReferenceLabels(new[] { "Milo", "Mimi", "  ", "" });

        Assert.Equal(new[] { "Milo", "Mimi" }, scene.RelevantReferenceLabels);
    }

    [Fact]
    public void SetRelevantReferenceLabels_deduplicates_case_insensitively_keeping_first_seen()
    {
        var storyboard = Storyboard.Create(Guid.NewGuid());
        var scene = storyboard.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);

        scene.SetRelevantReferenceLabels(new[] { "Milo", "milo", "MILO", "Mimi" });

        Assert.Equal(new[] { "Milo", "Mimi" }, scene.RelevantReferenceLabels);
    }

    [Fact]
    public void SetRelevantReferenceLabels_with_empty_input_clears_it()
    {
        var storyboard = Storyboard.Create(Guid.NewGuid());
        var scene = storyboard.AddScene(8, "narration", "visual", "push", SceneVisualType.AiVideo);
        scene.SetRelevantReferenceLabels(new[] { "Milo" });

        scene.SetRelevantReferenceLabels(Array.Empty<string>());

        Assert.Empty(scene.RelevantReferenceLabels);
        Assert.Null(scene.RelevantReferenceLabelsText);
    }
}
