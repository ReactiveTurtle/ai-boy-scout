using BoyScout.Application.Projects;
using BoyScout.Common.Persistence;
using BoyScout.Infrastructure.Persistence;
using BoyScout.Infrastructure.Persistence.Repositories;
using BoyScout.Infrastructure.Projects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoyScout.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBoyScoutInfrastructure(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        services.AddDbContext<BoyScoutDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IProjectStore, ProjectStore>();
        services.AddScoped<IGitRepositoryIdentityResolver, GitRepositoryIdentityResolver>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
