using AiContentFactory.Domain.Costs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("ai_usage_records");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Provider).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Model).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Operation).IsRequired().HasMaxLength(50);
        builder.Property(r => r.EstimatedCostUsd).HasColumnType("decimal(10,4)");

        builder.HasIndex(r => r.ContentProjectId);
        builder.HasIndex(r => r.CreatedAt);
    }
}
