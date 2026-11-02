using TodoGraph.Application.Projects;
using TodoGraph.Domain.Processes;
using TodoGraph.Domain.Projects;

namespace TodoGraph.Application.Processes;

public sealed class ProcessRunHandler(
    IProjectRepository projects,
    IProcessRepository processDefinitions,
    IProcessRunRepository processRuns)
{
    public async Task<ProcessRunDto?> StartAsync(
        Guid projectId,
        Guid projectTaskId,
        Guid processId,
        CancellationToken cancellationToken)
    {
        var task = await GetProjectTaskAsync(projectId, projectTaskId, cancellationToken);
        if (task is null) return null;

        var savedRun = await processRuns.GetForProjectTaskAsync(projectTaskId, cancellationToken);
        if (savedRun is not null)
        {
            task.RestoreProcessRun(savedRun);
            throw new InvalidOperationException("A ProjectTask can start only one ProcessRun.");
        }

        var process = await processDefinitions.GetByIdAsync(processId, cancellationToken);
        if (process is null) return null;

        task.StartProcess(process);
        var run = task.CurrentProcessRun!;
        await processRuns.SaveAsync(projectTaskId, run, cancellationToken);
        return ProcessRunDto.From(projectTaskId, task, run);
    }

    public async Task<ProcessRunDto?> GetAsync(Guid projectId, Guid projectTaskId, CancellationToken cancellationToken)
    {
        var task = await GetProjectTaskAsync(projectId, projectTaskId, cancellationToken);
        if (task is null) return null;

        var run = await AttachRunAsync(task, cancellationToken);
        return run is null ? null : ProcessRunDto.From(projectTaskId, task, run);
    }

    public async Task<ProcessRunDto?> SubmitStageResultAsync(
        Guid projectId,
        Guid projectTaskId,
        ProcessStageResult result,
        CancellationToken cancellationToken)
    {
        var task = await GetProjectTaskAsync(projectId, projectTaskId, cancellationToken);
        if (task is null) return null;

        var run = await AttachRunAsync(task, cancellationToken);
        if (run is null) return null;

        task.CompleteCurrentProcessStage(result);
        await processRuns.SaveAsync(projectTaskId, run, cancellationToken);
        return ProcessRunDto.From(projectTaskId, task, run);
    }

    public async Task<ProcessRunDto?> SubmitUserResponseAsync(
        Guid projectId,
        Guid projectTaskId,
        string response,
        CancellationToken cancellationToken)
    {
        var task = await GetProjectTaskAsync(projectId, projectTaskId, cancellationToken);
        if (task is null) return null;

        var run = await AttachRunAsync(task, cancellationToken);
        if (run is null) return null;

        task.SubmitProcessUserResponse(response);
        await processRuns.SaveAsync(projectTaskId, run, cancellationToken);
        return ProcessRunDto.From(projectTaskId, task, run);
    }

    private async Task<ProjectTask?> GetProjectTaskAsync(Guid projectId, Guid projectTaskId, CancellationToken cancellationToken)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        return project?.Tasks.SingleOrDefault(task => task.Id == projectTaskId);
    }

    private async Task<ProcessRun?> AttachRunAsync(ProjectTask task, CancellationToken cancellationToken)
    {
        var run = await processRuns.GetForProjectTaskAsync(task.Id, cancellationToken);
        if (run is not null) task.RestoreProcessRun(run);
        return run;
    }
}
