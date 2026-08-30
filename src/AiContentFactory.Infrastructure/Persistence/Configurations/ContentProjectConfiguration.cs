using AiContentFactory.Domain.ContentProjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class ContentProjectConfiguration : IEntityTypeConfiguration<ContentProject>
{
    public void Configure(EntityTypeBuilder<ContentProject> builder)
    {
        builder.ToTable("content_projects");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Topic)
            .HasMaxLength(500);

        builder.Property(p => p.Niche)
            .HasMaxLength(200);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.AspectRatio)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.Language)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasIndex(p => p.Status);
    }
}
