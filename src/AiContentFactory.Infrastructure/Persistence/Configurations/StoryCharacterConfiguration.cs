using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Stories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class StoryCharacterConfiguration : IEntityTypeConfiguration<StoryCharacter>
{
    public void Configure(EntityTypeBuilder<StoryCharacter> builder)
    {
        builder.ToTable("story_characters");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.Description).HasColumnType("text");
        builder.Property(c => c.VisualDescription).HasColumnType("text");

        builder.Property(c => c.ReferenceImageStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.BehaviorProfile)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CharacterBehaviorProfile.None)
            .IsRequired();

        builder.Property(c => c.ReferenceImagePath).HasMaxLength(1000);
        builder.Property(c => c.ReferenceImagePrompt).HasColumnType("text");
        builder.Property(c => c.ReferenceImageProvider).HasMaxLength(100);

        builder.Property(c => c.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CharacterKind.Unspecified)
            .IsRequired();

        builder.Property(c => c.Species).HasMaxLength(StoryCharacter.SpeciesMaxLength);
        builder.Property(c => c.ClothingAndAccessories).HasMaxLength(StoryCharacter.ClothingAndAccessoriesMaxLength);
        builder.Property(c => c.DistinctiveFeatures).HasMaxLength(StoryCharacter.DistinctiveFeaturesMaxLength);

        builder.Property(c => c.ReferenceImageSource)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ReferenceImageSource.Generated)
            .IsRequired();

        // Candidate slot: written instead of the ReferenceImage* fields while the current image is Approved.
        builder.Property(c => c.PendingReferenceImagePath).HasMaxLength(1000);
        builder.Property(c => c.PendingReferenceImagePrompt).HasColumnType("text");
        builder.Property(c => c.PendingReferenceImageProvider).HasMaxLength(100);
        builder.Property(c => c.PendingReferenceImageSource)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Ignore(c => c.HasPendingReferenceImage);

        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        builder.HasIndex(c => c.StoryId);
    }
}
