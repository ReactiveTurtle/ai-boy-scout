using TodoGraph.Application.Processes;
using TodoGraph.Domain.Processes;

namespace TodoGraph.Api.Endpoints;

public sealed record StartProcessRunRequest(Guid ProcessId);
public sealed record SubmitProcessStageResultRequest(string? Title, string? Description, string? UserPrompt);
public sealed record SubmitProcessUserResponseRequest(string Response);

public static class ProcessRunEndpoints
{
    public static IEndpointRouteBuilder MapProcessRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/projects/{projectId:guid}/tasks/{taskId:guid}/process-run", async Task<IResult> (
            Guid projectId,
            Guid taskId,
            StartProcessRunRequest request,
            ProcessRunHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var run = await handler.StartAsync(projectId, taskId, request.ProcessId, cancellationToken);
                return run is null
                    ? Results.NotFound()
                    : Results.Created($"/api/projects/{projectId}/tasks/{taskId}/process-run", run);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        });

        endpoints.MapGet("/api/projects/{projectId:guid}/tasks/{taskId:guid}/process-run", (
            Guid projectId,
            Guid taskId,
            ProcessRunHandler handler,
            CancellationToken cancellationToken) =>
            ExecuteAsync(() => handler.GetAsync(projectId, taskId, cancellationToken)));

        endpoints.MapPost("/api/projects/{projectId:guid}/tasks/{taskId:guid}/process-run/current-stage-result", (
            Guid projectId,
            Guid taskId,
            SubmitProcessStageResultRequest request,
            ProcessRunHandler handler,
            CancellationToken cancellationToken) =>
            ExecuteAsync(() => handler.SubmitStageResultAsync(
                projectId,
                taskId,
                new ProcessStageResult(request.Title, request.Description, request.UserPrompt),
                cancellationToken)));

        endpoints.MapPost("/api/projects/{projectId:guid}/tasks/{taskId:guid}/process-run/user-response", (
            Guid projectId,
            Guid taskId,
            SubmitProcessUserResponseRequest request,
            ProcessRunHandler handler,
            CancellationToken cancellationToken) =>
            ExecuteAsync(() => handler.SubmitUserResponseAsync(projectId, taskId, request.Response, cancellationToken)));

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<ProcessRunDto?>> operation)
    {
        try
        {
            var run = await operation();
            return run is null ? Results.NotFound() : Results.Ok(run);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }
}
