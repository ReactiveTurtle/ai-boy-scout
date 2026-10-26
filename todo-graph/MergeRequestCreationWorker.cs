namespace TodoGraph;

public sealed class MergeRequestCreationWorker(
    CandidateStore store, IMergeRequestCreator creator, ITaskGitOperations git,
    ILogger<MergeRequestCreationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "MR creation queue failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        store.ReuseOpenMergeRequests();
        store.BlockInactiveMergeRequests();
        if (!creator.IsConfigured) return;
        var task = store.TryClaimMergeRequestCreation();
        if (task is null) return;
        try
        {
            if (!await git.IsPushedAsync(task, cancellationToken))
                throw new InvalidOperationException("The remote branch no longer matches the saved commit. Check the human push before creating an MR.");
            var url = await creator.FindOrCreateAsync(task, cancellationToken);
            if (!store.RecordCreatedMergeRequest(task.Id, url))
                logger.LogWarning("MR {Url} exists, but task {TaskId} changed locally. Reconcile before retrying.", url, task.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            store.FailMergeRequestCreation(task.Id, error.Message);
            logger.LogError(error, "MR creation failed for task {TaskId}.", task.Id);
        }
    }
}
