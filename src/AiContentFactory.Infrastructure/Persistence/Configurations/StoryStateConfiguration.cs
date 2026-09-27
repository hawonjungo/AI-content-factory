using AiContentFactory.Domain.Stories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class StoryStateConfiguration : IEntityTypeConfiguration<StoryState>
{
    public void Configure(EntityTypeBuilder<StoryState> builder)
    {
        builder.ToTable("story_states");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.CurrentLocation).HasMaxLength(300);
        builder.Property(s => s.CurrentObjective).HasColumnType("text");
        builder.Property(s => s.CharacterStates).HasColumnType("text");
        builder.Property(s => s.ImportantEventsText).HasColumnType("text");
        builder.Property(s => s.OpenStoryThreadsText).HasColumnType("text");
        builder.Property(s => s.UnresolvedConflictsText).HasColumnType("text");
        builder.Property(s => s.KnownFactsText).HasColumnType("text");
        builder.Property(s => s.NextPlannedDestination).HasMaxLength(300);
        builder.Property(s => s.Notes).HasColumnType("text");

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // Explicit unique index for the 1:1 constraint with Story (the
        // relationship itself is configured from the Story side via
        // HasForeignKey<StoryState>, which also implies uniqueness - this is
        // kept explicit for readability, mirroring StoryboardConfiguration's
        // HasIndex(s => s.ContentProjectId).IsUnique()).
        builder.HasIndex(s => s.StoryId).IsUnique();
    }
}
