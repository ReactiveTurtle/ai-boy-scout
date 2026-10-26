namespace TodoGraph;

public sealed class TaskWorktreeWorker(CandidateStore store, ITaskWorktreePreparer preparer, ILogger<TaskWorktreeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Task worktree preparation queue failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var task = store.TryClaimWorktreePreparation();
        if (task is null) return;
        try
        {
            var baseBranch = await preparer.PrepareAsync(task, cancellationToken);
            if (!store.FinishWorktreePreparation(task.Id, baseBranch))
                logger.LogWarning("Worktree for task {TaskId} was created but the task state changed; inspect {WorktreePath}.", task.Id, task.WorktreePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Restart recovery marks the reserved path as blocked; do not create another worktree.
        }
        catch (Exception error)
        {
            store.FailWorktreePreparation(task.Id, error.Message);
            logger.LogError(error, "Worktree preparation failed for task {TaskId}.", task.Id);
        }
    }
}
