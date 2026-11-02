using Microsoft.EntityFrameworkCore;
using TodoGraph.Domain.Processes;
using TodoGraph.Domain.Projects;
using TodoGraph.Infrastructure.Persistence.Entities;

namespace TodoGraph.Infrastructure.Persistence;

public sealed class TodoGraphDbContext(DbContextOptions<TodoGraphDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<ProcessRunRecord> ProcessRuns => Set<ProcessRunRecord>();
    public DbSet<ProcessRunBlockRecord> ProcessRunBlocks => Set<ProcessRunBlockRecord>();
    public DbSet<ProcessRunStageRecord> ProcessRunStages => Set<ProcessRunStageRecord>();
    public DbSet<ProcessStageSnapshotRecord> ProcessStageSnapshots => Set<ProcessStageSnapshotRecord>();
    public DbSet<ProcessRunLinkCounterRecord> ProcessRunLinkCounters => Set<ProcessRunLinkCounterRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TodoGraphDbContext).Assembly);
}
