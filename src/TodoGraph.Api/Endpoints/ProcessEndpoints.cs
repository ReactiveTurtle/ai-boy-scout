using TodoGraph.Application.Processes;

namespace TodoGraph.Api.Endpoints;

public sealed record CreateProcessRequest(string Name, string InitialStageInstructions);
public sealed record RenameProcessRequest(string Name);
public sealed record AddProcessBlockRequest(string InitialStageInstructions);
public sealed record ProcessInstructionsRequest(string Instructions);
public sealed record ReorderProcessBlocksRequest(IReadOnlyList<Guid> BlockIds);
public sealed record ReorderProcessStagesRequest(IReadOnlyList<Guid> StageIds);
public sealed record SetProcessStageBackwardLinkRequest(Guid TargetStageId, int MaxRepeats);

public static class ProcessEndpoints
{
    public static IEndpointRouteBuilder MapProcessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/processes", async (
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            Results.Ok(await handler.ListAsync(cancellationToken)));

        endpoints.MapGet("/api/processes/{processId:guid}", async (
            Guid processId,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
        {
            var process = await handler.GetAsync(processId, cancellationToken);
            return process is null ? Results.NotFound() : Results.Ok(process);
        });

        endpoints.MapPost("/api/processes", async Task<IResult> (
            CreateProcessRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var process = await handler.CreateAsync(request.Name, request.InitialStageInstructions, cancellationToken);
                return Results.Created($"/api/processes/{process.Id}", process);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        endpoints.MapPut("/api/processes/{processId:guid}", (
            Guid processId,
            RenameProcessRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.RenameAsync(processId, request.Name, cancellationToken)));

        endpoints.MapPost("/api/processes/{processId:guid}/blocks", (
            Guid processId,
            AddProcessBlockRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.AddBlockAsync(processId, request.InitialStageInstructions, cancellationToken)));

        endpoints.MapDelete("/api/processes/{processId:guid}/blocks/{blockId:guid}", (
            Guid processId,
            Guid blockId,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.RemoveBlockAsync(processId, blockId, cancellationToken)));

        endpoints.MapPut("/api/processes/{processId:guid}/blocks/order", (
            Guid processId,
            ReorderProcessBlocksRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.ReorderBlocksAsync(processId, request.BlockIds, cancellationToken)));

        endpoints.MapPost("/api/processes/{processId:guid}/blocks/{blockId:guid}/stages", (
            Guid processId,
            Guid blockId,
            ProcessInstructionsRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.AddStageAsync(processId, blockId, request.Instructions, cancellationToken)));

        endpoints.MapPut("/api/processes/{processId:guid}/blocks/{blockId:guid}/stages/order", (
            Guid processId,
            Guid blockId,
            ReorderProcessStagesRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.ReorderStagesAsync(processId, blockId, request.StageIds, cancellationToken)));

        endpoints.MapPut("/api/processes/{processId:guid}/blocks/{blockId:guid}/stages/{stageId:guid}", (
            Guid processId,
            Guid blockId,
            Guid stageId,
            ProcessInstructionsRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.UpdateStageInstructionsAsync(processId, blockId, stageId, request.Instructions, cancellationToken)));

        endpoints.MapDelete("/api/processes/{processId:guid}/blocks/{blockId:guid}/stages/{stageId:guid}", (
            Guid processId,
            Guid blockId,
            Guid stageId,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.RemoveStageAsync(processId, blockId, stageId, cancellationToken)));

        endpoints.MapPut("/api/processes/{processId:guid}/blocks/{blockId:guid}/stages/{stageId:guid}/backward-link", (
            Guid processId,
            Guid blockId,
            Guid stageId,
            SetProcessStageBackwardLinkRequest request,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.SetBackwardLinkAsync(
                processId,
                blockId,
                stageId,
                request.TargetStageId,
                request.MaxRepeats,
                cancellationToken)));

        endpoints.MapDelete("/api/processes/{processId:guid}/blocks/{blockId:guid}/stages/{stageId:guid}/backward-link", (
            Guid processId,
            Guid blockId,
            Guid stageId,
            ProcessCatalogHandler handler,
            CancellationToken cancellationToken) =>
            MutateAsync(() => handler.RemoveBackwardLinkAsync(processId, blockId, stageId, cancellationToken)));

        return endpoints;
    }

    private static async Task<IResult> MutateAsync(Func<Task<ProcessDto?>> mutation)
    {
        try
        {
            var process = await mutation();
            return process is null ? Results.NotFound() : Results.Ok(process);
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
