using AiContentFactory.Domain.Generation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class GenerationAttemptConfiguration : IEntityTypeConfiguration<GenerationAttempt>
{
    public void Configure(EntityTypeBuilder<GenerationAttempt> builder)
    {
        builder.ToTable("generation_attempts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(a => a.Provider).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Model).IsRequired().HasMaxLength(100);
        builder.Property(a => a.ModelTier).HasMaxLength(20);
        builder.Property(a => a.FailureReason).HasColumnType("text");

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        builder.HasIndex(a => a.ContentProjectId);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.ContentProjectId, a.SceneId, a.Kind });
    }
}
