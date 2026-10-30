using BoyScout.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace BoyScout.Infrastructure.Persistence;

public sealed class BoyScoutDbContext(DbContextOptions<BoyScoutDbContext> options) : DbContext(options)
{
    public DbSet<BoyScoutProject> Projects => Set<BoyScoutProject>();
    public DbSet<BoyScoutProjectPath> ProjectPaths => Set<BoyScoutProjectPath>();
    public DbSet<ProjectReportProjection> Reports => Set<ProjectReportProjection>();
    public DbSet<ProjectAiAccess> ProjectAiAccess => Set<ProjectAiAccess>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BoyScoutDbContext).Assembly);
}

public sealed class ProjectReportProjection
{
    public long Sequence { get; set; }
    public long? ProjectId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class ProjectAiAccess
{
    public long ProjectId { get; set; }
    public string GrantedAt { get; set; } = string.Empty;
}
