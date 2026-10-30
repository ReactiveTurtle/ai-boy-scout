using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoGraph.Infrastructure.Persistence;

public sealed class TodoGraphDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TodoGraphDbContext>
{
    public TodoGraphDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), $"todo-graph-design-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TodoGraphDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new TodoGraphDbContext(options);
    }
}
