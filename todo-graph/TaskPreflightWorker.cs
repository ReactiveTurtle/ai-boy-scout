namespace TodoGraph;

public sealed class TaskPreflightWorker(CandidateStore store, ITaskPreflightSource source, ILogger<TaskPreflightWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Task clarification queue failed."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var task = store.TryClaimTaskPreflight();
        if (task is null) return;
        try
        {
            var previous = store.GetTaskPreflight(task.Id);
            var result = await source.ExplainAsync(task, previous, cancellationToken);
            var evidence = result.Evidence.ToList();
            var questions = result.Questions.ToList();
            string? buildCommand = null;
            string? baseBranch = null;
            try
            {
                var plan = ProjectBuildRunner.DetectBuild(task.RepositoryPath!);
                buildCommand = $"{plan.Executable} {string.Join(' ', plan.Arguments)}";
                evidence.Add($"Команда проверки, которую обнаружил исполнитель: {buildCommand}");
            }
            catch (InvalidOperationException error)
            {
                questions.Add(error.Message);
            }
            try
            {
                baseBranch = task.MergeRequestStatus == MergeRequestStatus.Open && task.MergeRequestSourceBranch is not null
                    ? task.MergeRequestSourceBranch
                    : await TaskWorktreePreparer.DetectBaseBranchAsync(task.RepositoryPath!, cancellationToken);
                evidence.Add($"Базовая ветка для worktree: {baseBranch}");
            }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException)
            {
                questions.Add($"Не удалось определить базовую ветку: {error.Message}");
            }
            result = result with
            {
                Evidence = evidence.Take(20).ToArray(),
                Questions = questions.Take(20).ToArray(),
                BuildCommand = buildCommand,
                BaseBranch = baseBranch,
                Decomposition = questions.Count == 0 && (previous is null || !string.IsNullOrWhiteSpace(previous.Answers))
                    ? result.Decomposition : null,
            };
            store.SaveTaskPreflight(task.Id, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Startup migration requeues a clarification interrupted by shutdown.
        }
        catch (Exception error)
        {
            store.FailTaskPreflight(task.Id, error.Message);
            logger.LogError(error, "Task clarification failed for task {TaskId}.", task.Id);
        }
    }
}
