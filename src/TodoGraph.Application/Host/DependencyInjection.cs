using Microsoft.Extensions.DependencyInjection;
using TodoGraph.Application.Projects.Commands;
using TodoGraph.Application.Projects.Queries;

namespace TodoGraph.Application.Host;

public static class DependencyInjection
{
    public static IServiceCollection AddTodoGraphApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateProjectCommandHandler>();
        services.AddScoped<ListProjectsQueryHandler>();
        return services;
    }
}
