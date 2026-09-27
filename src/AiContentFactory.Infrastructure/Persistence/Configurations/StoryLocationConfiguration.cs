using AiContentFactory.Domain.Stories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class StoryLocationConfiguration : IEntityTypeConfiguration<StoryLocation>
{
    public void Configure(EntityTypeBuilder<StoryLocation> builder)
    {
        builder.ToTable("story_locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(l => l.Description).HasColumnType("text");
        builder.Property(l => l.VisualDescription).HasColumnType("text");

        builder.Property(l => l.ReferenceImageStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.ReferenceImagePath).HasMaxLength(1000);
        builder.Property(l => l.ReferenceImagePrompt).HasColumnType("text");
        builder.Property(l => l.ReferenceImageProvider).HasMaxLength(100);

        builder.Property(l => l.CreatedAt).IsRequired();
        builder.Property(l => l.UpdatedAt).IsRequired();

        builder.HasIndex(l => l.StoryId);
    }
}
