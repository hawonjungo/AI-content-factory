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

        // Preset ids reference the code-defined catalog (Application/Presets),
        // so there is no FK to enforce and no seed table to keep in sync.
        builder.Property(p => p.TemplateId).HasMaxLength(60);
        builder.Property(p => p.StylePresetId).HasMaxLength(60);
        builder.Property(p => p.VoicePresetId).HasMaxLength(60);
        builder.Property(p => p.CaptionPresetId).HasMaxLength(60);

        // Owned types mapped to jsonb: both are read and written whole, never
        // queried by their individual fields, so a column each beats 15 more
        // columns on content_projects.
        builder.OwnsOne(p => p.Captions, captions =>
        {
            captions.ToJson();
        });

        builder.OwnsOne(p => p.Progress, progress =>
        {
            progress.ToJson();
        });

        builder.OwnsOne(p => p.IdeaConfig, idea =>
        {
            idea.ToJson();
        });

        builder.OwnsOne(p => p.LastRenderValidation, validation =>
        {
            validation.ToJson();
        });

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasIndex(p => p.Status);
    }
}
