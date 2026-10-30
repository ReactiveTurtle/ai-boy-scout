using Microsoft.EntityFrameworkCore;
using TodoGraph.Domain.Processes;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Infrastructure.Persistence;

public sealed class TodoGraphDbContext(DbContextOptions<TodoGraphDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Process> Processes => Set<Process>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TodoGraphDbContext).Assembly);
}
