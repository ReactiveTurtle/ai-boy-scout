using BoyScout.Core.Tasks;

namespace BoyScout.Api.Ai;

internal sealed class ProposalGenerationService(
    TaskStore taskStore,
    OpenCodeProposalGenerator generator,
    AiProcessingControl processingControl,
    ILogger<ProposalGenerationService> logger) : BackgroundService
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(1);
    private const int MaximumFailureLength = 300;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var generationLease = processingControl.TryAcquireGeneration(stoppingToken);
            if (generationLease is null)
            {
                await Task.Delay(DisabledDelay, stoppingToken);
                continue;
            }

            TaskProposal? proposal = null;
            try
            {
                generationLease.Token.ThrowIfCancellationRequested();
                proposal = taskStore.GetProcessingProposal() ?? taskStore.TryClaimNextProposal();
                if (proposal is null)
                {
                    await Task.Delay(EmptyQueueDelay, generationLease.Token);
                    continue;
                }

                var generated = await generator.GenerateAsync(proposal, generationLease.Token);
                generationLease.Token.ThrowIfCancellationRequested();
                CompleteProposal(taskStore, proposal, generated);

                logger.LogInformation(
                    "Proposal {ProposalId} for report {ReportId} received verdict {Relevance}.",
                    proposal.Id,
                    proposal.ReportId,
                    generated.Relevance);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (generationLease.Token.IsCancellationRequested)
            {
                if (proposal is not null)
                {
                    logger.LogInformation(
                        "Proposal {ProposalId} generation paused; processing state was preserved.",
                        proposal.Id);
                }
            }
            catch (Exception exception) when (proposal is not null)
            {
                var failure = SafeFailure(exception);
                try
                {
                    taskStore.FailProposal(proposal.Id, failure);
                    logger.LogWarning(
                        "Proposal {ProposalId} for report {ReportId} failed: {Failure}",
                        proposal.Id,
                        proposal.ReportId,
                        failure);
                }
                catch (Exception persistenceException)
                {
                    logger.LogError(
                        "Could not persist failure for proposal {ProposalId}: {Failure}",
                        proposal.Id,
                        SafeFailure(persistenceException));
                }
            }
        }
    }

    internal static TaskProposal CompleteProposal(
        TaskStore taskStore,
        TaskProposal proposal,
        GeneratedProposal generated)
    {
        return generated.Relevance switch
        {
            TaskProposalRelevances.Current => taskStore.SaveProposal(
                proposal.Id,
                generated.Title!,
                generated.Description!,
                generated.AcceptanceCriteria!,
                generated.VerificationSummary,
                generated.LocalizedReportTitle,
                generated.LocalizedReportObservation,
                generated.LocalizedReportImpact,
                generated.LocalizedReportSuggestedAction),
            TaskProposalRelevances.Obsolete =>
                taskStore.SaveRejectionRecommendation(
                    proposal.Id,
                    generated.FinalRejectionReason!,
                    generated.VerificationSummary,
                    generated.LocalizedReportTitle,
                    generated.LocalizedReportObservation,
                    generated.LocalizedReportImpact,
                    generated.LocalizedReportSuggestedAction),
            TaskProposalRelevances.Unverified =>
                taskStore.MarkUnverified(
                    proposal.Id,
                    generated.VerificationSummary,
                    localizedReportTitle: generated.LocalizedReportTitle,
                    localizedReportObservation: generated.LocalizedReportObservation,
                    localizedReportImpact: generated.LocalizedReportImpact,
                    localizedReportSuggestedAction: generated.LocalizedReportSuggestedAction),
            _ => throw new InvalidOperationException("Unsupported proposal relevance."),
        };
    }

    private static string SafeFailure(Exception exception)
    {
        var message = $"{exception.GetType().Name}: {exception.Message}"
            .ReplaceLineEndings(" ")
            .Trim();
        return message.Length <= MaximumFailureLength
            ? message
            : message[..MaximumFailureLength];
    }
}
