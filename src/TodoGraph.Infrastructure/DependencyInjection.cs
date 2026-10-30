using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoGraph.Application.Processes;
using TodoGraph.Application.Projects;
using TodoGraph.Infrastructure.Persistence;
using TodoGraph.Infrastructure.Persistence.Repositories;

namespace TodoGraph.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTodoGraphInfrastructure(this IServiceCollection services, string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        services.AddDbContext<TodoGraphDbContext>(options => options.UseSqlite(
            connectionString,
            sqlite => sqlite.MigrationsAssembly(typeof(TodoGraphDbContext).Assembly.GetName().Name)));
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IProcessRepository, EfProcessRepository>();
        return services;
    }
}
