using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
