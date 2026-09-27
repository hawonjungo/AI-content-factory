using AiContentFactory.Domain.AssetReferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class AssetReferenceConfiguration : IEntityTypeConfiguration<AssetReference>
{
    public void Configure(EntityTypeBuilder<AssetReference> builder)
    {
        builder.ToTable("asset_references");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ImagePath).HasMaxLength(1000);
        builder.Property(r => r.Provider).HasMaxLength(100);
        builder.Property(r => r.Prompt).HasColumnType("text");
        builder.Property(r => r.Label).HasMaxLength(200);

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        builder.HasIndex(r => new { r.ContentProjectId, r.Type });
        builder.HasIndex(r => new { r.ContentProjectId, r.Type, r.Label });
    }
}
