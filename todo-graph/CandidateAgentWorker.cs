namespace TodoGraph;

public sealed class CandidateAgentWorker(
    CandidateStore store,
    TodoGraphAgent agent,
    ILogger<CandidateAgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var candidateId = store.TryClaimAgentJob();
            if (candidateId is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            try
            {
                await agent.AnalyzeAsync(candidateId.Value, stoppingToken);
                store.SetAgentJobState(candidateId.Value, "ready");
                logger.LogInformation("Agent review finished for candidate {CandidateId}.", candidateId.Value);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                store.SetAgentJobState(candidateId.Value, "queued");
                break;
            }
            catch (Exception error)
            {
                store.SetAgentJobState(candidateId.Value, "failed", error.Message);
                logger.LogError(error, "Agent review failed for candidate {CandidateId}.", candidateId.Value);
            }
        }
    }
}
