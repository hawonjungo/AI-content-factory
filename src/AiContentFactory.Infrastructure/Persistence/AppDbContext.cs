using AiContentFactory.Domain.AssetReferences;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Common;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Costs;
using AiContentFactory.Domain.Generation;
using AiContentFactory.Domain.Publishing;
using AiContentFactory.Domain.Qa;
using AiContentFactory.Domain.Scripts;
using AiContentFactory.Domain.Storyboards;
using Microsoft.EntityFrameworkCore;

namespace AiContentFactory.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ContentProject> ContentProjects => Set<ContentProject>();
    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<Storyboard> Storyboards => Set<Storyboard>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetReference> AssetReferences => Set<AssetReference>();
    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();
    public DbSet<VideoGenerationRecord> VideoGenerationRecords => Set<VideoGenerationRecord>();
    public DbSet<GenerationAttempt> GenerationAttempts => Set<GenerationAttempt>();
    public DbSet<QaScore> QaScores => Set<QaScore>();
    public DbSet<SocialConnection> SocialConnections => Set<SocialConnection>();
    public DbSet<PublishJob> PublishJobs => Set<PublishJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Every aggregate here sets its own Id in BaseEntity's initializer
        // (Guid.NewGuid()). EF Core's convention for a Guid key is
        // ValueGeneratedOnAdd, which makes it decide "new vs existing" from
        // whether the key is empty - so a freshly-built entity added to an
        // already-tracked parent (e.g. a new Scene on an existing Storyboard
        // during clip-plan regeneration) is misread as Modified and emitted as
        // an UPDATE that matches 0 rows. Telling EF the key is never
        // store-generated makes it use graph position instead: new to the
        // graph => Added.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.Id))
                    .ValueGeneratedNever();
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
