using AiContentFactory.Domain.Storyboards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class StoryboardConfiguration : IEntityTypeConfiguration<Storyboard>
{
    public void Configure(EntityTypeBuilder<Storyboard> builder)
    {
        builder.ToTable("storyboards");

        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.ContentProjectId).IsUnique();

        // Scenes is a read-only wrapper over the private `_scenes` field.
        // Point EF at the backing field directly rather than the property.
        builder.Metadata
            .FindNavigation(nameof(Storyboard.Scenes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Scenes)
            .WithOne()
            .HasForeignKey(sc => sc.StoryboardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SceneConfiguration : IEntityTypeConfiguration<Scene>
{
    public void Configure(EntityTypeBuilder<Scene> builder)
    {
        builder.ToTable("scenes");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Narration).HasColumnType("text");
        builder.Property(s => s.VisualDescription).HasColumnType("text");
        builder.Property(s => s.CameraDirection).HasColumnType("text");
        builder.Property(s => s.GenerationPrompt).HasColumnType("text");
        builder.Property(s => s.NegativePrompt).HasColumnType("text");

        builder.Property(s => s.VisualType).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(s => new { s.StoryboardId, s.SceneNumber }).IsUnique();
    }
}
