namespace TodoGraph;

public sealed class TaskImplementationWorker(
    CandidateStore store,
    ITaskImplementationAgent developer,
    IProjectBuildRunner buildRunner,
    ILogger<TaskImplementationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Task development queue failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var task = store.TryClaimImplementation();
        if (task is null) return;
        var review = store.GetTaskPreflight(task.Id);
        var reviewComments = task.MergeRequestStatus == MergeRequestStatus.Open ? store.ListPendingReviewComments(task.Id) : [];
        string? buildFailure = null;
        string? lastFailure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var current = store.GetTaskNode(task.Id);
                if (current?.Status == "closed") return;
                if (current?.MergeRequestStatus is MergeRequestStatus.Closed or MergeRequestStatus.Merged)
                {
                    store.BlockImplementation(task.Id, "Связанный MR закрыт или слит. Проверьте MR и решите, продолжать ли задачу.");
                    return;
                }
                var turn = await developer.ImplementAsync(task, review, reviewComments, buildFailure, cancellationToken);
                if (turn.Action == "question")
                {
                    store.AwaitDevelopmentClarification(task.Id, turn.Question!);
                    return;
                }
                if (turn.Action == "reply_only")
                {
                    if (!string.IsNullOrWhiteSpace(task.MergeRequestUrl))
                        store.CompleteReplyOnlyReview(task.Id, task.MergeRequestUrl, reviewComments.Select(comment => comment.Id).ToArray());
                    else
                        store.BlockImplementation(task.Id, "Agent requested a reply-only review, but the task has no open MR URL.");
                    return;
                }
                if (!await buildRunner.HasChangesAsync(task, cancellationToken))
                {
                    store.BlockImplementation(task.Id, "No worktree changes were made. Check the task or answer the agent's question before resuming.");
                    return;
                }

                store.SetTaskBuildState(task.Id, ProjectBuildStatus.Running);
                TaskBuildResult build;
                try { build = await buildRunner.BuildAsync(task, review, cancellationToken); }
                catch (InvalidOperationException error)
                {
                    store.AwaitDevelopmentClarification(task.Id, error.Message);
                    return;
                }
                if (build.Succeeded)
                {
                    var latest = store.GetTaskNode(task.Id);
                    if (latest?.MergeRequestStatus is MergeRequestStatus.Closed or MergeRequestStatus.Merged)
                    {
                        store.BlockImplementation(task.Id, "Во время сборки связанный MR закрыли или слили. Проверьте состояние перед продолжением.");
                        return;
                    }
                    store.SetTaskBuildState(task.Id, ProjectBuildStatus.Passed);
                    if (!string.IsNullOrWhiteSpace(task.MergeRequestUrl) && reviewComments.Count > 0)
                        store.MarkReviewCommentsHandled(task.Id, task.MergeRequestUrl, reviewComments.Select(comment => comment.Id).ToArray());
                    store.CompleteImplementation(task.Id);
                    logger.LogInformation("Task {TaskId} passed its detected project check {Command}.", task.Id, build.Command);
                    return;
                }

                buildFailure = $"{build.Command} exited with code {build.ExitCode}: {build.Output}";
                store.SetTaskBuildState(task.Id, ProjectBuildStatus.Failed, buildFailure);
                if (buildFailure == lastFailure)
                {
                    store.AwaitDevelopmentClarification(task.Id, "The same build failure persisted after another fix. Clarify the cause before continuing: " + buildFailure);
                    return;
                }
                lastFailure = buildFailure;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Startup recovery blocks interrupted work to preserve the worktree and ask for inspection.
        }
        catch (Exception error)
        {
            store.BlockImplementation(task.Id, error.Message);
            logger.LogError(error, "Implementation failed for task {TaskId}.", task.Id);
        }
    }
}
