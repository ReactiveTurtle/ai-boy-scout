using TodoGraph.Domain.Processes;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Processes;

public sealed record ProcessRunDto(
    Guid Id,
    Guid ProjectTaskId,
    Guid ProcessId,
    string ProcessName,
    ProcessRunStatus Status,
    Guid? CurrentBlockId,
    Guid? CurrentStageId,
    string? CurrentUserPrompt,
    string? PendingUserResponse,
    string? Error,
    string? Title,
    string Description)
{
    public static ProcessRunDto From(Guid projectTaskId, ProjectTask task, ProcessRun run) => new(
        run.Id,
        projectTaskId,
        run.Snapshot.ProcessId,
        run.Snapshot.Name,
        run.Status,
        run.CurrentBlockId,
        run.CurrentStageId,
        run.CurrentUserPrompt,
        run.PendingUserResponse,
        run.Error,
        task.Title,
        task.Description);
}
