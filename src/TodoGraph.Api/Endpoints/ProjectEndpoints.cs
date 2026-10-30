using Microsoft.AspNetCore.SignalR;
using TodoGraph.Api.Hubs;
using TodoGraph.Application.Projects;
using TodoGraph.Application.Projects.Commands;
using TodoGraph.Application.Projects.Queries;

namespace TodoGraph.Api.Endpoints;

public sealed record CreateProjectRequest(string Name, string FullPath);

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/projects", async (
            ListProjectsQueryHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.Handle(new ListProjectsQuery(), cancellationToken)));

        endpoints.MapPost("/api/projects", async Task<IResult> (
            CreateProjectRequest request,
            CreateProjectCommandHandler handler,
            IHubContext<ProjectHub> hub,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            ProjectDto project;
            try
            {
                project = await handler.Handle(new CreateProjectCommand(request.Name, request.FullPath), cancellationToken);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }

            try
            {
                await hub.Clients.All.SendAsync(ProjectHub.ProjectCreatedEvent, project, cancellationToken);
            }
            catch (Exception exception)
            {
                loggerFactory.CreateLogger("ProjectSignalR").LogWarning(
                    exception,
                    "Project {ProjectId} was created, but its SignalR notification could not be sent.",
                    project.Id);
            }

            return Results.Created($"/api/projects/{project.Id}", project);
        });

        return endpoints;
    }
}
