using AiContentFactory.Domain.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class SocialConnectionConfiguration : IEntityTypeConfiguration<SocialConnection>
{
    public void Configure(EntityTypeBuilder<SocialConnection> builder)
    {
        builder.ToTable("social_connections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Platform).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.ExternalAccountId).HasMaxLength(200);
        builder.Property(c => c.ExternalAccountName).HasMaxLength(200);
        builder.Property(c => c.AccessTokenEncrypted).HasColumnType("text");
        builder.Property(c => c.RefreshTokenEncrypted).HasColumnType("text");
        builder.Property(c => c.Scope).HasColumnType("text");
        builder.Property(c => c.PendingSelectionData).HasColumnType("text");

        // Single-tenant: at most one connection row per platform.
        builder.HasIndex(c => c.Platform).IsUnique();
    }
}

public class PublishJobConfiguration : IEntityTypeConfiguration<PublishJob>
{
    public void Configure(EntityTypeBuilder<PublishJob> builder)
    {
        builder.ToTable("publish_jobs");
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Platform).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(j => j.VideoPath).HasMaxLength(1000).IsRequired();
        builder.Property(j => j.Title).HasMaxLength(500);
        builder.Property(j => j.Caption).HasColumnType("text");
        builder.Property(j => j.Hashtags).HasColumnType("text");
        builder.Property(j => j.ExternalPostId).HasMaxLength(300);
        builder.Property(j => j.PublishedUrl).HasMaxLength(1000);
        builder.Property(j => j.ErrorMessage).HasColumnType("text");
        builder.Property(j => j.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(j => j.Privacy).HasMaxLength(50);

        builder.HasIndex(j => j.ContentProjectId);
        builder.HasIndex(j => new { j.ContentProjectId, j.Platform });
        builder.HasIndex(j => j.Status);
    }
}
