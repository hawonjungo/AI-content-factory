using AiContentFactory.Domain.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiContentFactory.Infrastructure.Persistence.Configurations;

public class ScriptConfiguration : IEntityTypeConfiguration<Script>
{
    public void Configure(EntityTypeBuilder<Script> builder)
    {
        builder.ToTable("scripts");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Hook).HasColumnType("text");
        builder.Property(s => s.Introduction).HasColumnType("text");
        builder.Property(s => s.Body).HasColumnType("text");
        builder.Property(s => s.Escalation).HasColumnType("text");
        builder.Property(s => s.Payoff).HasColumnType("text");
        builder.Property(s => s.CallToAction).HasColumnType("text");

        builder.HasIndex(s => s.ContentProjectId).IsUnique();
    }
}
