using AiContentFactory.Domain.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.FilePath).HasMaxLength(1000);
        builder.Property(a => a.Provider).HasMaxLength(100);
        builder.Property(a => a.Prompt).HasColumnType("text");

        builder.HasIndex(a => a.ContentProjectId);
        builder.HasIndex(a => a.SceneId);
    }
}
