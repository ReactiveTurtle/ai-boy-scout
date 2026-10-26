namespace TodoGraph;

public sealed class MergeRequestReplyWorker(
    CandidateStore store,
    IMergeRequestReplyPublisher agent,
    ILogger<MergeRequestReplyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "MR reply queue processing failed.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    internal async Task ProcessQueueOnceAsync(CancellationToken cancellationToken)
    {
        if (!agent.IsConfigured) return;
        while (!cancellationToken.IsCancellationRequested && store.TryClaimMergeRequestReply() is { } reply)
        {
            try
            {
                var replyId = await agent.SendReplyAsync(reply, cancellationToken);
                if (!store.MarkMergeRequestReplySent(reply, replyId))
                    logger.LogWarning("GitLab accepted reply {ReplyId}, but local reply state for task {TaskId} was not updated.", replyId, reply.TaskId);
                else
                    logger.LogInformation("Published confirmed MR reply {ReplyId} for task {TaskId}.", replyId, reply.TaskId);
            }
            catch (Exception error)
            {
                store.MarkMergeRequestReplyFailed(reply, "Delivery could not be confirmed; verify GitLab before resubmitting. " + error.Message);
                logger.LogError(error, "Could not confirm delivery of MR reply for task {TaskId}.", reply.TaskId);
                if (cancellationToken.IsCancellationRequested) break;
            }
        }
    }
}
