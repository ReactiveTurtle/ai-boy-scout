namespace TodoGraph;

public sealed class MergeRequestReviewWorker(
    CandidateStore store,
    IMergeRequestCommentSource agent,
    ILogger<MergeRequestReviewWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "MR comment polling cycle failed.");
            }

            try { await Task.Delay(ReadInterval(logger), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var task in store.ListPollableMergeRequests())
        {
            if (!agent.IsConfiguredFor(task))
            {
                logger.LogDebug("MR comment polling is idle for task {TaskId} until the project's {CodeHost} read MCP is configured.", task.Id, task.CodeHost);
                continue;
            }
            try
            {
                var review = await agent.FetchCommentsAsync(task, cancellationToken);
                if (store.SaveMergeRequestReview(task.Id, task.MergeRequestUrl!, review))
                    logger.LogInformation("Polled {CommentCount} comments from MR {MrState} for task {TaskId}.", review.Comments.Count, review.Status, task.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Could not poll MR comments for task {TaskId}.", task.Id);
            }
        }
    }

    private static TimeSpan ReadInterval(ILogger logger)
    {
        var configured = Environment.GetEnvironmentVariable("TODO_GRAPH_MR_POLL_INTERVAL_SECONDS");
        if (string.IsNullOrWhiteSpace(configured)) return TimeSpan.FromSeconds(60);
        if (int.TryParse(configured, out var seconds) && seconds is >= 15 and <= 3600)
            return TimeSpan.FromSeconds(seconds);
        logger.LogWarning("TODO_GRAPH_MR_POLL_INTERVAL_SECONDS must be between 15 and 3600; using 60 seconds.");
        return TimeSpan.FromSeconds(60);
    }
}
