using AiContentFactory.Domain.Stories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("stories");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Premise).HasColumnType("text");
        builder.Property(s => s.Niche).HasMaxLength(200);

        builder.Property(s => s.Language)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(s => s.AspectRatio)
            .IsRequired()
            .HasMaxLength(10);

        // Nullable series-level style preset; mirrors ContentProject.StylePresetId.
        builder.Property(s => s.StylePresetId).HasMaxLength(Story.MaxStylePresetIdLength);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // Characters/Locations/Episodes are read-only wrappers over private
        // backing fields - point EF at the fields directly, same as
        // Storyboard.Scenes.
        builder.Metadata.FindNavigation(nameof(Story.Characters))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Story.Locations))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Story.Episodes))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        // Genuinely-owned child rows: deleting a Story deletes its
        // characters/locations/episodes.
        builder.HasMany(s => s.Characters)
            .WithOne()
            .HasForeignKey(c => c.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Locations)
            .WithOne()
            .HasForeignKey(l => l.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(s => s.Episodes)
            .WithOne()
            .HasForeignKey(e => e.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1:1, principal is Story; StoryState carries the FK + unique index
        // (see StoryStateConfiguration).
        builder.HasOne(s => s.State)
            .WithOne()
            .HasForeignKey<StoryState>(st => st.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reusable "story bible" - jsonb, nullable (null until
        // Story.SetBible is called), mirrors StoryStateSnapshot's OwnsOne.
        builder.OwnsOne(s => s.Bible, bible =>
        {
            bible.ToJson();
        });
    }
}
