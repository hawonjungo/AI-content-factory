using AiContentFactory.Domain.Qa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class QaScoreConfiguration : IEntityTypeConfiguration<QaScore>
{
    public void Configure(EntityTypeBuilder<QaScore> builder)
    {
        builder.ToTable("qa_scores");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Notes).HasColumnType("text");

        builder.HasIndex(s => s.ContentProjectId);
    }
}
