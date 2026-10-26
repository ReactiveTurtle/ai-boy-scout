namespace TodoGraph;

public sealed class TaskCommitWorker(CandidateStore store, ITaskGitOperations git, IProjectBuildRunner buildRunner, ILogger<TaskCommitWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Task commit queue failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (store.TryClaimTaskCommit() is { } task)
        {
            try
            {
                if (!store.InvalidateTaskBuildForRebase(task.Id))
                    throw new InvalidOperationException("Task changed before rebase; inspect it before committing.");
                await git.RebaseAsync(task, cancellationToken);
                store.SetTaskBuildState(task.Id, ProjectBuildStatus.Running);
                TaskBuildResult build;
                try { build = await buildRunner.BuildAsync(task, store.GetTaskPreflight(task.Id), cancellationToken); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    store.SetTaskBuildState(task.Id, ProjectBuildStatus.Failed, error.Message);
                    throw new InvalidOperationException($"Post-rebase build could not run in '{task.WorktreePath}'. Fix the build issue and resume; no local commit was created. {error.Message}", error);
                }
                if (!build.Succeeded)
                {
                    var failure = $"Post-rebase build failed ({build.Command}, exit {build.ExitCode}) in '{task.WorktreePath}'. Fix the build issue and resume; no local commit was created. {build.Output}";
                    store.SetTaskBuildState(task.Id, ProjectBuildStatus.Failed, failure);
                    throw new InvalidOperationException(failure);
                }
                store.SetTaskBuildState(task.Id, ProjectBuildStatus.Passed);
                var sha = await git.CommitAsync(task, cancellationToken);
                if (!store.RecordTaskCommit(task.Id, sha))
                    logger.LogWarning("Task {TaskId} was committed as {Sha}, but its state changed; inspect the worktree.", task.Id, sha);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception error)
            {
                store.FailTaskCommit(task.Id, error.Message);
                logger.LogError(error, "Local commit failed for task {TaskId}.", task.Id);
            }
        }

        foreach (var waiting in store.ListAwaitingPush())
        {
            try
            {
                if (await git.IsPushedAsync(waiting, cancellationToken))
                    store.RecordVerifiedPush(waiting.Id, waiting.CommitSha!);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Could not verify push for task {TaskId}.", waiting.Id); }
        }
    }
}
