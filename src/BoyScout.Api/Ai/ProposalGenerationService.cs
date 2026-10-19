using BoyScout.Core.Tasks;
using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using BoyScout.Core.Storage.Config;
using BoyScout.Core.Storage.Firebase;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;

namespace BoyScout.Api.Ai;

internal sealed class ProposalGenerationService(
    ITaskStore taskStore,
    OpenCodeProposalGenerator generator,
    AiProcessingControl processingControl,
    BoyScoutConfigService config,
    ILogger<ProposalGenerationService> logger) : BackgroundService
{
    private static readonly TimeSpan EmptyQueueDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirebaseLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FirebaseLeaseRenewalInterval = TimeSpan.FromSeconds(30);
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
            FirebaseClaimContext? firebaseClaim = null;
            try
            {
                generationLease.Token.ThrowIfCancellationRequested();
                proposal = taskStore.GetProcessingProposal() ?? taskStore.TryClaimNextProposal();
                if (proposal is null)
                {
                    firebaseClaim = await ClaimFirebaseProposalAsync(generationLease.Token);
                    if (firebaseClaim is null)
                    {
                        await Task.Delay(EmptyQueueDelay, generationLease.Token);
                        continue;
                    }

                    proposal = firebaseClaim.Proposal;
                }

                var generated = firebaseClaim is null
                    ? await generator.GenerateAsync(proposal, generationLease.Token)
                    : await GenerateWithRenewingLeaseAsync(proposal, firebaseClaim, generationLease.Token);
                generationLease.Token.ThrowIfCancellationRequested();
                if (firebaseClaim is null)
                {
                    CompleteProposal(taskStore, proposal, generated);
                }
                else
                {
                    await SaveFirebaseOutcomeAsync(firebaseClaim, generated, generationLease.Token);
                }

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
            catch (FirebaseLeaseLostException exception)
            {
                logger.LogWarning("Firebase lease for report {ReportId} was lost; generated result was discarded: {Failure}",
                    proposal?.ReportId, SafeFailure(exception));
            }
            catch (Exception exception) when (proposal is not null)
            {
                var failure = SafeFailure(exception);
                try
                {
                    if (firebaseClaim is null)
                    {
                        taskStore.FailProposal(proposal.Id, failure);
                    }
                    else
                    {
                        await firebaseClaim.Repository.FailProposalAsync(
                            firebaseClaim.Lease, proposal.ReportId, failure, generationLease.Token);
                    }
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
            finally
            {
                firebaseClaim?.Dispose();
            }
        }
    }

    internal static TaskProposal MapFirebaseClaim(FirebaseTaskClaim claim, string reportProject)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(claim.Note);
        ArgumentNullException.ThrowIfNull(claim.Proposal);
        if (string.IsNullOrWhiteSpace(reportProject)) throw new InvalidOperationException("Firebase binding path is invalid.");

        var note = claim.Note;
        var source = claim.Proposal;
        var reportId = RequiredNoteText(note, "Id");
        var originalPath = RequiredNoteText(note, "OriginalPath");
        var noteCreatedAt = RequiredNoteText(note, "CreatedAt");
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!DateTimeOffset.TryParse(noteCreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !string.Equals(ProjectPathNormalizer.Normalize(originalPath), ProjectPathNormalizer.Normalize(reportProject), pathComparison))
            throw new InvalidOperationException("Firebase note path or creation timestamp is invalid.");
        var parts = reportId.Split('-');
        if (parts.Length != 3 || parts[0] != "BSR" ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var projectId) || projectId <= 0 ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0 ||
            reportId != $"BSR-{projectId.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D19", CultureInfo.InvariantCulture)}" ||
            source.ReportId != reportId || source.ProjectId != projectId || claim.Lease.ReportId != reportId ||
            note["ProjectId"] is not JsonValue noteProject ||
            !noteProject.TryGetValue<long>(out var noteProjectId) || noteProjectId != projectId)
            throw new InvalidOperationException("Firebase claim identity is invalid.");

        if (!DateTimeOffset.TryParse(source.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !DateTimeOffset.TryParse(source.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            source.Attempts < 0)
            throw new InvalidOperationException("Firebase proposal fields are invalid.");

        var reviewIntent = source.ReviewIntent ?? TaskProposalReviewIntents.Proposal;
        if (reviewIntent is not (TaskProposalReviewIntents.Proposal or TaskProposalReviewIntents.Rejection))
            throw new InvalidOperationException("Firebase review intent is invalid.");
        return new TaskProposal(
            sequence, reportId, projectId, reportProject, source.State, reviewIntent,
            source.RequestedRejectionReason, source.RequestedClarification, source.Relevance,
            source.RecommendedAction, source.FinalRejectionReason, source.VerificationSummary,
            source.Title, source.Description, source.AcceptanceCriteria, source.Error, source.Attempts,
            source.CreatedAt, source.UpdatedAt,
            RequiredNoteText(note, "Title"), OptionalNoteText(note, "Location"),
            RequiredNoteText(note, "Observation"), OptionalNoteText(note, "Impact"),
            OptionalNoteText(note, "SuggestedAction"), OptionalNoteText(note, "DiscoveredDuring"),
            source.LocalizedReportTitle, source.LocalizedReportObservation, source.LocalizedReportImpact,
            source.LocalizedReportSuggestedAction, source.AgentCheckedAt, source.HumanApprovedAt,
            source.HumanRejectedAt, source.CurrentConsequences);
    }

    private async Task<FirebaseClaimContext?> ClaimFirebaseProposalAsync(CancellationToken cancellationToken)
    {
        var worker = config.LoadStatus();
        if (!worker.HasFirebaseConnection) return null;
        FirebaseRealtimeDatabaseTransport transport;
        try
        {
            transport = new FirebaseRealtimeDatabaseTransport(config.LoadConfiguredFirebaseConnection());
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            logger.LogWarning("Firebase credentials are unavailable for note processing: {Failure}", SafeFailure(exception));
            return null;
        }

        var repository = new FirebaseTaskRepository(transport);
        try
        {
            foreach (var binding in config.ListBindings().Where(binding => binding.AiReadConsent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var claim = await repository.ClaimNextEligibleNoteAsync(binding,
                        worker.InstallationId.ToString("D"), worker.DisplayName,
                        FirebaseLeaseDuration, cancellationToken);
                    if (claim is not null)
                    {
                        return new FirebaseClaimContext(transport, repository, claim.Lease,
                            MapFirebaseClaim(claim, binding.LocalPath));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning("Could not claim a Firebase note for project {ProjectId}: {Failure}",
                        binding.ProjectId, SafeFailure(exception));
                }
            }
        }
        catch
        {
            transport.Dispose();
            throw;
        }
        transport.Dispose();
        return null;
    }

    private async Task<GeneratedProposal> GenerateWithRenewingLeaseAsync(
        TaskProposal proposal, FirebaseClaimContext claim, CancellationToken cancellationToken)
    {
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Exception? renewalFailure = null;
        var renewalTask = RenewLeaseAsync(claim, generation, exception => renewalFailure = exception);
        GeneratedProposal? generated = null;
        Exception? generationFailure = null;
        try
        {
            generated = await generator.GenerateAsync(proposal, generation.Token);
        }
        catch (Exception exception) { generationFailure = exception; }
        finally
        {
            generation.Cancel();
            await renewalTask;
        }

        if (renewalFailure is not null) throw new FirebaseLeaseLostException(renewalFailure);
        if (generationFailure is not null) ExceptionDispatchInfo.Capture(generationFailure).Throw();
        return generated!;
    }

    private static async Task RenewLeaseAsync(FirebaseClaimContext claim, CancellationTokenSource generation,
        Action<Exception> onFailure)
    {
        try
        {
            while (true)
            {
                await Task.Delay(FirebaseLeaseRenewalInterval, generation.Token);
                var renewed = await claim.RepositoryLease.RenewAsync(claim.Lease, generation.Token);
                if (renewed is null) throw new FirebaseLeaseLostException();
                claim.Lease = renewed;
            }
        }
        catch (OperationCanceledException) when (generation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            onFailure(exception);
            generation.Cancel();
        }
    }

    private static async Task SaveFirebaseOutcomeAsync(FirebaseClaimContext claim, GeneratedProposal generated,
        CancellationToken cancellationToken)
    {
        var proposal = claim.Proposal;
        switch (generated.Relevance)
        {
            case TaskProposalRelevances.Current:
                await claim.Repository.SaveCurrentProposalAsync(claim.Lease, proposal.ReportId,
                    generated.Title!, generated.Description!, generated.AcceptanceCriteria!, generated.VerificationSummary,
                    cancellationToken, generated.CurrentConsequences, generated.LocalizedReportTitle,
                    generated.LocalizedReportObservation, generated.LocalizedReportImpact,
                    generated.LocalizedReportSuggestedAction);
                break;
            case TaskProposalRelevances.Obsolete:
                await claim.Repository.SaveRejectionRecommendationAsync(claim.Lease, proposal.ReportId,
                    generated.FinalRejectionReason!, generated.VerificationSummary!, cancellationToken,
                    generated.LocalizedReportTitle, generated.LocalizedReportObservation,
                    generated.LocalizedReportImpact, generated.LocalizedReportSuggestedAction);
                break;
            case TaskProposalRelevances.Unverified:
                await claim.Repository.MarkUnverifiedAsync(claim.Lease, proposal.ReportId,
                    generated.VerificationSummary!, cancellationToken: cancellationToken,
                    localizedReportTitle: generated.LocalizedReportTitle,
                    localizedReportObservation: generated.LocalizedReportObservation,
                    localizedReportImpact: generated.LocalizedReportImpact,
                    localizedReportSuggestedAction: generated.LocalizedReportSuggestedAction);
                break;
            default:
                throw new InvalidOperationException("Unsupported proposal relevance.");
        }
    }

    private static string RequiredNoteText(JsonObject note, string key) =>
        OptionalNoteText(note, key) ?? throw new InvalidOperationException($"Firebase note field {key} is required.");

    private static string? OptionalNoteText(JsonObject note, string key)
    {
        if (!note.ContainsKey(key) || note[key] is null) return null;
        if (note[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            return text;
        throw new InvalidOperationException($"Firebase note field {key} is invalid.");
    }

    internal static TaskProposal CompleteProposal(
        ITaskStore taskStore,
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
                generated.LocalizedReportSuggestedAction,
                currentConsequences: generated.CurrentConsequences),
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

    private sealed class FirebaseClaimContext(FirebaseRealtimeDatabaseTransport transport, FirebaseTaskRepository repository,
        FirebaseProjectLeaseHandle lease, TaskProposal proposal)
    {
        public FirebaseTaskRepository Repository { get; } = repository;
        public FirebaseProjectLease RepositoryLease { get; } = new(transport, FirebaseLeaseDuration);
        public FirebaseProjectLeaseHandle Lease { get; set; } = lease;
        public TaskProposal Proposal { get; } = proposal;
        public void Dispose() => transport.Dispose();
    }

    private sealed class FirebaseLeaseLostException(Exception? inner = null)
        : Exception("Firebase processing lease was lost.", inner);
}
