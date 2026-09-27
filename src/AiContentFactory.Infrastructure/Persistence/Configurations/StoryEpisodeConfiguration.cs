using AiContentFactory.Domain.Stories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class StoryEpisodeConfiguration : IEntityTypeConfiguration<StoryEpisode>
{
    public void Configure(EntityTypeBuilder<StoryEpisode> builder)
    {
        builder.ToTable("story_episodes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Script).HasColumnType("text");
        builder.Property(e => e.Summary).HasColumnType("text");

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        // Self-referencing FK: Restrict, not Cascade - a cascade path through
        // a self-reference creates a delete cycle that PostgreSQL rejects,
        // and semantically deleting one episode should never silently take
        // out its successor.
        builder.HasOne<StoryEpisode>()
            .WithMany()
            .HasForeignKey(e => e.PreviousEpisodeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Point-in-time copy of StoryState, not a live FK - jsonb, nullable
        // (a freshly-created episode has no snapshot until Complete()).
        builder.OwnsOne(e => e.StoryStateSnapshot, snapshot =>
        {
            snapshot.ToJson();
        });

        // Production plan written before the script itself - jsonb, nullable
        // (null until StoryEpisode.SetOutline is called).
        builder.OwnsOne(e => e.Outline, outline =>
        {
            outline.ToJson();
        });

        // Non-unique: nothing in this pass enforces episode-number
        // uniqueness within a story (unlike Scene.SceneNumber on
        // Storyboard), so this is a lookup index only.
        builder.HasIndex(e => new { e.StoryId, e.EpisodeNumber });

        // Plain nullable Guid, no relational FK to content_projects - the Story
        // module is intentionally decoupled from the ContentProjects module in
        // both directions. The application layer keeps this consistent; a real
        // FK would also fight with ContentProjectsController.Delete's own
        // cascade-delete logic, which knows nothing about Stories.
        builder.Property(e => e.ContentProjectId);
        builder.HasIndex(e => e.ContentProjectId);
    }
}
