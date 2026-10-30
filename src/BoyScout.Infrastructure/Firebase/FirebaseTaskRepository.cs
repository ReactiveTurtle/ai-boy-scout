using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoyScout.Application.Reports;
using BoyScout.Infrastructure.Configuration;
using BoyScout.Infrastructure.Persistence;
using BoyScout.Domain.Reports;
using BoyScout.Domain.Tasks;

namespace BoyScout.Infrastructure.Firebase;

public sealed record FirebaseTaskProposal(
    string ReportId, long ProjectId, string State, string? Relevance, string? RecommendedAction,
    string? Title, string? Description, string? AcceptanceCriteria, string? VerificationSummary,
    string CreatedAt, string UpdatedAt, string? AgentCheckedAt = null,
    string? HumanApprovedAt = null, string? HumanRejectedAt = null,
    string? CurrentConsequences = null, string? ReviewIntent = null,
    string? RequestedRejectionReason = null, string? RequestedClarification = null,
    string? FinalRejectionReason = null, string? Error = null, int Attempts = 0,
    string? LocalizedReportTitle = null, string? LocalizedReportObservation = null,
    string? LocalizedReportImpact = null, string? LocalizedReportSuggestedAction = null);

public sealed record FirebaseTaskPage(IReadOnlyList<BoyScoutTask> Items, string? NextCursor);
public sealed record FirebaseTaskClaim(JsonObject Note, FirebaseTaskProposal Proposal, FirebaseProjectLeaseHandle Lease);
public sealed record FirebaseTaskReprocessResult(JsonObject Note, FirebaseTaskProposal Proposal, string? DeletedTaskId);

// Standalone workflow: neither task creation nor API integration happens without explicit confirmation.
public sealed class FirebaseTaskRepository(FirebaseRealtimeDatabaseTransport transport)
{
    private const int MaxAttempts = 5;
    private const int MaxCommentLength = 2_000;

    public async Task<FirebaseTaskClaim?> ClaimNextEligibleNoteAsync(ProjectBinding binding,
        string workerInstallationId, string workerName, TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(transport);
        if (!binding.AiReadConsent || !Directory.Exists(binding.LocalPath)) return null;
        Required(workerInstallationId, nameof(workerInstallationId));
        Required(workerName, nameof(workerName));
        var localPath = ProjectPathNormalizer.Normalize(binding.LocalPath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var projectId = binding.ProjectId;
        if (projectId <= 0) return null;
        var path = ProjectPath(projectId);
        var lease = new FirebaseProjectLease(transport, leaseDuration);
        var initial = ReadProject((await transport.GetAsync(path, cancellationToken)).Json, projectId);
        var notes = (JsonObject)initial["Notes"]!;
        var proposals = GetCollection(initial, "TaskProposals");
        var tasks = GetCollection(initial, "Tasks");
        var candidates = new List<(string Id, DateTimeOffset CreatedAt)>();
        foreach (var entry in notes)
        {
            var note = (JsonObject)entry.Value!;
            if (!Active(note) || !HasText(note, "OriginalPath") || !HasText(note, "CreatedAt") ||
                !DateTimeOffset.TryParse((string)note["CreatedAt"]!, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var createdAt)) continue;
            string remotePath;
            try { remotePath = ProjectPathNormalizer.Normalize((string)note["OriginalPath"]!); }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { continue; }
            if (!string.Equals(remotePath, localPath, pathComparison) ||
                tasks.ContainsKey(TaskId(entry.Key)) || HasLinkedTask(tasks, entry.Key)) continue;
            if (proposals.TryGetPropertyValue(entry.Key, out var proposalNode) &&
                ReadProposal(proposalNode, entry.Key, projectId).State is not (TaskProposalStates.Queued or TaskProposalStates.Processing)) continue;
            candidates.Add((entry.Key, createdAt));
        }

        foreach (var candidate in candidates.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            var handle = await lease.AcquireAsync(candidate.Id, workerInstallationId, workerName, cancellationToken);
            if (handle is null) continue;
            var candidatePath = ProjectPath(projectId);
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var snapshot = await transport.GetAsync(candidatePath, cancellationToken);
                var root = ReadProject(snapshot.Json, projectId);
                if (!FirebaseProjectLease.IsValid(root, handle, FirebaseProjectLease.ServerTime(snapshot))) break;
                var notesNow = (JsonObject)root["Notes"]!;
                if (!notesNow.TryGetPropertyValue(candidate.Id, out var noteValue) || noteValue is not JsonObject note ||
                    !Active(note) || !HasText(note, "OriginalPath")) break;
                string remotePath;
                try { remotePath = ProjectPathNormalizer.Normalize((string)note["OriginalPath"]!); }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) { break; }
                if (!string.Equals(remotePath, localPath, pathComparison)) break;
                var currentTasks = GetCollection(root, "Tasks");
                if (currentTasks.ContainsKey(TaskId(candidate.Id)) || HasLinkedTask(currentTasks, candidate.Id)) break;
                var currentProposals = GetCollection(root, "TaskProposals");
                JsonObject claimed;
                FirebaseTaskProposal proposal;
                if (currentProposals.TryGetPropertyValue(candidate.Id, out var existing))
                {
                    proposal = ReadProposal(existing, candidate.Id, projectId);
                    if (proposal.State is not (TaskProposalStates.Queued or TaskProposalStates.Processing)) break;
                    claimed = ((JsonObject)existing!).DeepClone().AsObject();
                    claimed["State"] = TaskProposalStates.Processing;
                    claimed["UpdatedAt"] = Timestamp();
                    claimed["Attempts"] = proposal.Attempts;
                }
                else
                {
                    var timestamp = Timestamp();
                    proposal = new FirebaseTaskProposal(candidate.Id, projectId, TaskProposalStates.Processing,
                        null, null, null, null, null, null, timestamp, timestamp, Attempts: 1);
                    claimed = JsonSerializer.SerializeToNode(proposal)!.AsObject();
                }
                currentProposals[candidate.Id] = claimed;
                root["TaskProposals"] = currentProposals;
                if (await transport.PutIfMatchAsync(candidatePath, root.ToJsonString(), snapshot.ETag, cancellationToken))
                    return new FirebaseTaskClaim((JsonObject)note.DeepClone(), ReadProposal(claimed, candidate.Id, projectId), handle);
            }
        }
        return null;
    }

    public async Task<FirebaseTaskProposal> SaveReadyProposalAsync(FirebaseProjectLeaseHandle handle, string reportId, string title,
        string description, string acceptanceCriteria, string? verificationSummary,
        CancellationToken cancellationToken = default, string? currentConsequences = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(handle);
        var projectId = ParseProjectId(reportId);
        if (handle.ReportId != reportId) throw new ArgumentException("Lease belongs to a different note.", nameof(handle));
        Required(title, nameof(title));
        Required(description, nameof(description));
        Required(acceptanceCriteria, nameof(acceptanceCriteria));
        if (verificationSummary is not null) Required(verificationSummary, nameof(verificationSummary));
        if (currentConsequences is not null) Required(currentConsequences, nameof(currentConsequences));

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            if (!FirebaseProjectLease.IsValid(root, handle, FirebaseProjectLease.ServerTime(snapshot)))
                throw new InvalidOperationException("Processing lease is stale or expired; proposal was not saved.");
            if (!Active(note)) throw new InvalidOperationException("Only an active note can receive a proposal.");
            var proposals = GetCollection(root, "TaskProposals");
            var tasks = GetCollection(root, "Tasks");
            if (proposals.ContainsKey(reportId) || tasks.ContainsKey(TaskId(reportId)) || HasLinkedTask(tasks, reportId))
                throw new InvalidOperationException("A proposal or task already exists for this note.");

            var timestamp = Timestamp();
            var proposal = new FirebaseTaskProposal(reportId, projectId, TaskProposalStates.Ready,
                TaskProposalRelevances.Current, TaskProposalRecommendedActions.CreateTask,
                title.Trim(), description.Trim(), acceptanceCriteria.Trim(), verificationSummary?.Trim(),
                timestamp, timestamp, AgentCheckedAt: timestamp,
                CurrentConsequences: currentConsequences?.Trim());
            proposals[reportId] = JsonSerializer.SerializeToNode(proposal);
            root["TaskProposals"] = proposals;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return proposal;
        }
        throw new InvalidOperationException("Project remained contended; proposal was not saved.");
    }

    public Task<FirebaseTaskProposal> SaveCurrentProposalAsync(FirebaseProjectLeaseHandle handle, string reportId,
        string title, string description, string acceptanceCriteria, string? verificationSummary,
        CancellationToken cancellationToken = default, string? currentConsequences = null,
        string? localizedReportTitle = null, string? localizedReportObservation = null,
        string? localizedReportImpact = null, string? localizedReportSuggestedAction = null)
    {
        Required(title, nameof(title));
        Required(description, nameof(description));
        Required(acceptanceCriteria, nameof(acceptanceCriteria));
        OptionalRequired(verificationSummary, nameof(verificationSummary));
        OptionalRequired(currentConsequences, nameof(currentConsequences));
        ValidateLocalized(localizedReportTitle, localizedReportObservation, localizedReportImpact, localizedReportSuggestedAction);
        return WriteAgentResultAsync(handle, reportId, TaskProposalStates.Ready, TaskProposalRelevances.Current,
            TaskProposalRecommendedActions.CreateTask, cancellationToken, new()
            {
                ["Title"] = title.Trim(), ["Description"] = description.Trim(),
                ["AcceptanceCriteria"] = acceptanceCriteria.Trim(),
                ["VerificationSummary"] = Trim(verificationSummary), ["CurrentConsequences"] = Trim(currentConsequences),
                ["FinalRejectionReason"] = null, ["Error"] = null,
                ["LocalizedReportTitle"] = Trim(localizedReportTitle),
                ["LocalizedReportObservation"] = Trim(localizedReportObservation),
                ["LocalizedReportImpact"] = Trim(localizedReportImpact),
                ["LocalizedReportSuggestedAction"] = Trim(localizedReportSuggestedAction)
            });
    }

    public Task<FirebaseTaskProposal> SaveRejectionRecommendationAsync(FirebaseProjectLeaseHandle handle,
        string reportId, string finalRejectionReason, string verificationSummary,
        CancellationToken cancellationToken = default, string? localizedReportTitle = null,
        string? localizedReportObservation = null, string? localizedReportImpact = null,
        string? localizedReportSuggestedAction = null)
    {
        Required(finalRejectionReason, nameof(finalRejectionReason));
        Required(verificationSummary, nameof(verificationSummary));
        ValidateLocalized(localizedReportTitle, localizedReportObservation, localizedReportImpact, localizedReportSuggestedAction);
        return WriteAgentResultAsync(handle, reportId, TaskProposalStates.Ready, TaskProposalRelevances.Obsolete,
            TaskProposalRecommendedActions.Reject, cancellationToken, new()
            {
                ["Title"] = null, ["Description"] = null, ["AcceptanceCriteria"] = null,
                ["CurrentConsequences"] = null, ["FinalRejectionReason"] = finalRejectionReason.Trim(),
                ["VerificationSummary"] = verificationSummary.Trim(), ["Error"] = null,
                ["LocalizedReportTitle"] = Trim(localizedReportTitle),
                ["LocalizedReportObservation"] = Trim(localizedReportObservation),
                ["LocalizedReportImpact"] = Trim(localizedReportImpact),
                ["LocalizedReportSuggestedAction"] = Trim(localizedReportSuggestedAction)
            });
    }

    public Task<FirebaseTaskProposal> MarkUnverifiedAsync(FirebaseProjectLeaseHandle handle, string reportId,
        string summary, string relevance = TaskProposalRelevances.Unverified,
        CancellationToken cancellationToken = default, string? localizedReportTitle = null,
        string? localizedReportObservation = null, string? localizedReportImpact = null,
        string? localizedReportSuggestedAction = null)
    {
        Required(summary, nameof(summary));
        if (relevance is not (TaskProposalRelevances.Current or TaskProposalRelevances.Unverified))
            throw new ArgumentException("An unverified proposal must have current or unverified relevance.", nameof(relevance));
        ValidateLocalized(localizedReportTitle, localizedReportObservation, localizedReportImpact, localizedReportSuggestedAction);
        return WriteAgentResultAsync(handle, reportId, TaskProposalStates.Unverified, relevance, null,
            cancellationToken, new()
            {
                ["Title"] = null, ["Description"] = null, ["AcceptanceCriteria"] = null,
                ["CurrentConsequences"] = null, ["FinalRejectionReason"] = null,
                ["VerificationSummary"] = summary.Trim(), ["Error"] = null,
                ["LocalizedReportTitle"] = Trim(localizedReportTitle),
                ["LocalizedReportObservation"] = Trim(localizedReportObservation),
                ["LocalizedReportImpact"] = Trim(localizedReportImpact),
                ["LocalizedReportSuggestedAction"] = Trim(localizedReportSuggestedAction)
            });
    }

    public Task<FirebaseTaskProposal> FailProposalAsync(FirebaseProjectLeaseHandle handle, string reportId,
        string error, CancellationToken cancellationToken = default)
    {
        Required(error, nameof(error));
        return WriteAgentResultAsync(handle, reportId, TaskProposalStates.Failed, null, null,
            cancellationToken, new()
            {
                ["CurrentConsequences"] = null, ["Error"] = error.Trim()
            });
    }

    public Task<FirebaseTaskProposal> RetryProposalAsync(string reportId, string comment,
        CancellationToken cancellationToken = default) =>
        RequeueReviewProposalAsync(reportId, comment, clarification: false, cancellationToken);

    public Task<FirebaseTaskProposal> RequestClarificationAsync(string reportId, string instruction,
        CancellationToken cancellationToken = default) =>
        RequeueReviewProposalAsync(reportId, instruction, clarification: true, cancellationToken);

    public Task<FirebaseTaskProposal> RequestRejectionReviewAsync(string reportId, string reason,
        CancellationToken cancellationToken = default) =>
        RequeueReviewProposalAsync(reportId, reason, clarification: false, cancellationToken, rejectionReview: true);

    public Task<FirebaseTaskProposal> RequeueProcessingProposalAsync(string reportId, string comment,
        CancellationToken cancellationToken = default) =>
        RequeueProcessingAsync(reportId, comment, cancellationToken);

    public Task<FirebaseTaskProposal> RestoreRejectedNoteAsync(string reportId, string comment,
        CancellationToken cancellationToken = default) =>
        RestoreRejectedAsync(reportId, comment, cancellationToken);

    public async Task<FirebaseTaskReprocessResult> ReprocessCompletedNoteAsync(string reportId, string comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var projectId = ParseProjectId(reportId);
        Required(comment, nameof(comment));
        var normalizedComment = comment.Trim();
        if (normalizedComment.Length > MaxCommentLength)
            throw new ArgumentException($"Comment must not exceed {MaxCommentLength} characters.", nameof(comment));

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            if ((string)note["Status"]! != ReportStatuses.Resolved)
                throw new InvalidOperationException("Only a resolved note can be reprocessed.");

            var proposals = GetCollection(root, "TaskProposals");
            JsonNode? existingNode = null;
            FirebaseTaskProposal? existing = null;
            if (proposals.TryGetPropertyValue(reportId, out existingNode))
            {
                existing = ReadProposal(existingNode, reportId, projectId);
                if (existing.State != TaskProposalStates.Confirmed)
                    throw new InvalidOperationException("A resolved note can only be reprocessed without a proposal or with a confirmed proposal.");
            }

            var tasks = GetCollection(root, "Tasks");
            var linkedTasks = new List<(string Id, BoyScoutTask Task)>();
            foreach (var entry in tasks)
            {
                if (entry.Value is not JsonObject taskNode || !HasText(taskNode, "Id", entry.Key) ||
                    !HasLong(taskNode, "ProjectId", projectId) || !HasText(taskNode, "Title") ||
                    !HasText(taskNode, "Description") || !HasText(taskNode, "AcceptanceCriteria") ||
                    !HasText(taskNode, "Status") || !TaskStatuses.IsValid((string)taskNode["Status"]!) ||
                    !HasText(taskNode, "CreatedAt") || !HasText(taskNode, "UpdatedAt") ||
                    taskNode["SourceReportIds"] is not JsonArray sources || sources.Count == 0)
                    throw InvalidData();
                BoyScoutTask task;
                try { task = taskNode.Deserialize<BoyScoutTask>() ?? throw InvalidData(); }
                catch (JsonException) { throw InvalidData(); }
                try
                {
                    if (!entry.Key.StartsWith("BST-", StringComparison.Ordinal) ||
                        ParseProjectId(entry.Key[4..]) != projectId || task.SourceReportIds is null ||
                        task.SourceReportIds.Any(source => ParseProjectId(source) != projectId)) throw InvalidData();
                }
                catch (ArgumentException) { throw InvalidData(); }
                if (task.SourceReportIds.Contains(reportId)) linkedTasks.Add((entry.Key, task));
            }

            if (linkedTasks.Count > 1)
                throw new InvalidOperationException("A resolved note with multiple linked tasks cannot be reprocessed.");
            var linkedTask = linkedTasks.SingleOrDefault();
            if (linkedTasks.Count == 1 && linkedTask.Task.SourceReportIds.Count != 1)
                throw new InvalidOperationException("A task shared by multiple notes cannot be deleted for reprocessing.");

            var timestamp = Timestamp();
            JsonObject proposal;
            if (existing is null)
            {
                proposal = JsonSerializer.SerializeToNode(new FirebaseTaskProposal(reportId, projectId,
                    TaskProposalStates.Queued, null, null, null, null, null, null, timestamp, timestamp,
                    ReviewIntent: TaskProposalReviewIntents.Proposal, RequestedClarification: normalizedComment))!.AsObject();
            }
            else
            {
                proposal = ((JsonObject)existingNode!).DeepClone().AsObject();
                ResetProposalOutputs(proposal);
                foreach (var field in new[] { "LocalizedReportTitle", "LocalizedReportObservation", "LocalizedReportImpact", "LocalizedReportSuggestedAction" })
                    proposal[field] = null;
                proposal["State"] = TaskProposalStates.Queued;
                proposal["UpdatedAt"] = timestamp;
                proposal["ReviewIntent"] = TaskProposalReviewIntents.Proposal;
                proposal["RequestedRejectionReason"] = null;
                proposal["RequestedClarification"] = normalizedComment;
            }

            note["Status"] = ReportStatuses.Open;
            note["UpdatedAt"] = timestamp;
            FirebaseProjectLease.Invalidate(root, reportId, FirebaseProjectLease.ServerTime(snapshot));
            proposals[reportId] = proposal;
            root["TaskProposals"] = proposals;
            string? deletedTaskId = null;
            if (linkedTasks.Count == 1)
            {
                deletedTaskId = linkedTask.Id;
                tasks.Remove(linkedTask.Id);
                root["Tasks"] = tasks;
            }

            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return new FirebaseTaskReprocessResult((JsonObject)note.DeepClone(),
                    ReadProposal(proposal, reportId, projectId), deletedTaskId);
        }
        throw new InvalidOperationException("Project remained contended; note was not reprocessed.");
    }

    private async Task<FirebaseTaskProposal> RequeueReviewProposalAsync(string reportId, string comment,
        bool clarification, CancellationToken cancellationToken, bool rejectionReview = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var projectId = ParseProjectId(reportId);
        Required(comment, rejectionReview ? "reason" : clarification ? "instruction" : "comment");
        var normalizedComment = comment.Trim();
        if (normalizedComment.Length > MaxCommentLength)
            throw new ArgumentException($"Comment must not exceed {MaxCommentLength} characters.", rejectionReview ? "reason" : clarification ? "instruction" : "comment");

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            var proposals = GetCollection(root, "TaskProposals");
            if (!Active(note)) throw new InvalidOperationException("Only an active note can be reviewed for rejection.");
            if (!proposals.TryGetPropertyValue(reportId, out var value))
            {
                if (!rejectionReview)
                    throw new InvalidOperationException("Only an active note with a ready, unverified, or failed proposal can be requeued.");
                var createdAt = Timestamp();
                var created = new FirebaseTaskProposal(reportId, projectId, TaskProposalStates.Queued,
                    null, null, null, null, null, null, createdAt, createdAt,
                    ReviewIntent: TaskProposalReviewIntents.Rejection,
                    RequestedRejectionReason: normalizedComment, Attempts: 1);
                proposals[reportId] = JsonSerializer.SerializeToNode(created);
                root["TaskProposals"] = proposals;
                if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken)) return created;
                continue;
            }
            var existing = ReadProposal(value, reportId, projectId);
            if (existing.State is not (TaskProposalStates.Ready or TaskProposalStates.Unverified or TaskProposalStates.Failed))
                throw new InvalidOperationException("Only a ready, unverified, or failed proposal can be requeued.");
            if (existing.Attempts == int.MaxValue) throw new InvalidOperationException("Proposal attempt count exhausted.");

            var timestamp = Timestamp();
            var proposal = ((JsonObject)value!).DeepClone().AsObject();
            proposal["State"] = TaskProposalStates.Queued;
            proposal["UpdatedAt"] = timestamp;
            proposal["Attempts"] = existing.Attempts + 1;
            proposal["Title"] = null;
            proposal["Description"] = null;
            proposal["AcceptanceCriteria"] = null;
            proposal["CurrentConsequences"] = null;
            proposal["Relevance"] = null;
            proposal["VerificationSummary"] = null;
            proposal["RecommendedAction"] = null;
            proposal["FinalRejectionReason"] = null;
            proposal["Error"] = null;
            proposal["AgentCheckedAt"] = null;
            proposal["HumanApprovedAt"] = null;
            proposal["HumanRejectedAt"] = null;
            proposal["RequestedClarification"] = rejectionReview ? null : normalizedComment;
            if (clarification || rejectionReview)
            {
                proposal["ReviewIntent"] = rejectionReview ? TaskProposalReviewIntents.Rejection : TaskProposalReviewIntents.Proposal;
                proposal["RequestedRejectionReason"] = rejectionReview ? normalizedComment : null;
            }

            FirebaseProjectLease.Invalidate(root, reportId, FirebaseProjectLease.ServerTime(snapshot));
            proposals[reportId] = proposal;
            root["TaskProposals"] = proposals;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return ReadProposal(proposal, reportId, projectId);
        }
        throw new InvalidOperationException("Project remained contended; proposal was not requeued.");
    }

    private async Task<FirebaseTaskProposal> RequeueProcessingAsync(string reportId, string comment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var projectId = ParseProjectId(reportId);
        Required(comment, nameof(comment));
        var normalizedComment = comment.Trim();
        if (normalizedComment.Length > MaxCommentLength)
            throw new ArgumentException($"Comment must not exceed {MaxCommentLength} characters.", nameof(comment));

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            var proposals = GetCollection(root, "TaskProposals");
            if (!Active(note) || !proposals.TryGetPropertyValue(reportId, out var value) ||
                ReadProposal(value, reportId, projectId).State != TaskProposalStates.Processing)
                throw new InvalidOperationException("Only an active note with a processing proposal can be requeued.");

            var proposal = ((JsonObject)value!).DeepClone().AsObject();
            ResetProposalOutputs(proposal);
            proposal["State"] = TaskProposalStates.Queued;
            proposal["UpdatedAt"] = Timestamp();
            proposal["ReviewIntent"] = TaskProposalReviewIntents.Proposal;
            proposal["RequestedRejectionReason"] = null;
            proposal["RequestedClarification"] = normalizedComment;
            FirebaseProjectLease.Invalidate(root, reportId, FirebaseProjectLease.ServerTime(snapshot));
            proposals[reportId] = proposal;
            root["TaskProposals"] = proposals;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return ReadProposal(proposal, reportId, projectId);
        }
        throw new InvalidOperationException("Project remained contended; proposal was not requeued.");
    }

    private async Task<FirebaseTaskProposal> RestoreRejectedAsync(string reportId, string comment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var projectId = ParseProjectId(reportId);
        Required(comment, nameof(comment));
        var normalizedComment = comment.Trim();
        if (normalizedComment.Length > MaxCommentLength)
            throw new ArgumentException($"Comment must not exceed {MaxCommentLength} characters.", nameof(comment));

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            var proposals = GetCollection(root, "TaskProposals");
            if ((string)note["Status"]! != ReportStatuses.Dismissed)
                throw new InvalidOperationException("Only a dismissed note can be restored.");
            FirebaseTaskProposal? existing = null;
            JsonNode? existingNode = null;
            if (proposals.TryGetPropertyValue(reportId, out existingNode))
            {
                existing = ReadProposal(existingNode, reportId, projectId);
                if (existing.State != TaskProposalStates.Rejected)
                    throw new InvalidOperationException("A dismissed note can only be restored without a proposal or with a rejected proposal.");
            }

            var timestamp = Timestamp();
            JsonObject proposal;
            if (existing is null)
            {
                proposal = JsonSerializer.SerializeToNode(new FirebaseTaskProposal(reportId, projectId,
                    TaskProposalStates.Queued, null, null, null, null, null, null, timestamp, timestamp,
                    ReviewIntent: TaskProposalReviewIntents.Proposal, RequestedClarification: normalizedComment))!.AsObject();
            }
            else
            {
                proposal = ((JsonObject)existingNode!).DeepClone().AsObject();
                ResetProposalOutputs(proposal);
                proposal["State"] = TaskProposalStates.Queued;
                proposal["UpdatedAt"] = timestamp;
                proposal["ReviewIntent"] = TaskProposalReviewIntents.Proposal;
                proposal["RequestedRejectionReason"] = null;
                proposal["RequestedClarification"] = normalizedComment;
            }

            note["Status"] = ReportStatuses.Open;
            note["UpdatedAt"] = timestamp;
            FirebaseProjectLease.Invalidate(root, reportId, FirebaseProjectLease.ServerTime(snapshot));
            proposals[reportId] = proposal;
            root["TaskProposals"] = proposals;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return ReadProposal(proposal, reportId, projectId);
        }
        throw new InvalidOperationException("Project remained contended; note was not restored.");
    }

    private static void ResetProposalOutputs(JsonObject proposal)
    {
        foreach (var field in new[] { "Title", "Description", "AcceptanceCriteria", "CurrentConsequences",
                     "Relevance", "VerificationSummary", "RecommendedAction", "FinalRejectionReason", "Error",
                     "AgentCheckedAt", "HumanApprovedAt", "HumanRejectedAt" })
            proposal[field] = null;
    }

    private async Task<FirebaseTaskProposal> WriteAgentResultAsync(FirebaseProjectLeaseHandle handle,
        string reportId, string state, string? relevance, string? action, CancellationToken cancellationToken,
        JsonObject result)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(handle);
        var projectId = ParseProjectId(reportId);
        if (handle.ReportId != reportId) throw new ArgumentException("Lease belongs to a different note.", nameof(handle));
        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            if (!FirebaseProjectLease.IsValid(root, handle, FirebaseProjectLease.ServerTime(snapshot)))
                throw new InvalidOperationException("Processing lease is stale or expired; proposal was not saved.");
            if (!Active(note)) throw new InvalidOperationException("Only an active note can receive a proposal.");
            var proposals = GetCollection(root, "TaskProposals");
            var tasks = GetCollection(root, "Tasks");
            if (tasks.ContainsKey(TaskId(reportId)) || HasLinkedTask(tasks, reportId))
                throw new InvalidOperationException("A task already exists for this note.");
            if (!proposals.TryGetPropertyValue(reportId, out var existing))
                throw new InvalidOperationException("Only a processing proposal can receive an agent result.");
            var processingProposal = ReadProposal(existing, reportId, projectId);
            if (processingProposal.State != TaskProposalStates.Processing)
                throw new InvalidOperationException("Only a processing proposal can receive an agent result.");

            var timestamp = Timestamp();
            var proposal = ((JsonObject)existing!).DeepClone().AsObject();
            proposal["State"] = state;
            proposal["Relevance"] = relevance;
            proposal["RecommendedAction"] = action;
            proposal["UpdatedAt"] = timestamp;
            proposal["AgentCheckedAt"] = timestamp;
            proposal["HumanApprovedAt"] = null;
            proposal["HumanRejectedAt"] = null;
            foreach (var field in result) proposal[field.Key] = field.Value?.DeepClone();
            proposals[reportId] = proposal;
            root["TaskProposals"] = proposals;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return ReadProposal(proposal, reportId, projectId);
        }
        throw new InvalidOperationException("Project remained contended; proposal was not saved.");
    }

    public async Task<BoyScoutTask> ConfirmProposalAsync(string reportId, Guid confirmerInstallationId,
        string confirmerName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var projectId = ParseProjectId(reportId);
        if (confirmerInstallationId == Guid.Empty) throw new ArgumentException("Installation ID is required.", nameof(confirmerInstallationId));
        Required(confirmerName, nameof(confirmerName));

        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            var proposals = GetCollection(root, "TaskProposals");
            var tasks = GetCollection(root, "Tasks");
            if (!Active(note) || !proposals.TryGetPropertyValue(reportId, out var value))
                throw new InvalidOperationException("Only an active note with a ready proposal can be confirmed.");
            var proposal = ReadProposal(value, reportId, projectId);
            if (proposal.State != TaskProposalStates.Ready || proposal.Relevance != TaskProposalRelevances.Current ||
                proposal.RecommendedAction != TaskProposalRecommendedActions.CreateTask)
                throw new InvalidOperationException("Only a ready current create-task proposal can be confirmed.");
            var taskId = TaskId(reportId);
            if (tasks.ContainsKey(taskId) || HasLinkedTask(tasks, reportId))
                throw new InvalidOperationException("A task already exists for this note.");

            var timestamp = Timestamp();
            var task = new BoyScoutTask(taskId, projectId, proposal.Title!, proposal.Description!,
                proposal.AcceptanceCriteria!, TaskStatuses.Todo, timestamp, timestamp,
                [reportId], confirmerName.Trim(), confirmerInstallationId, proposal.CurrentConsequences);
            tasks[taskId] = JsonSerializer.SerializeToNode(task);
            root["Tasks"] = tasks;
            ((JsonObject)value!)["State"] = TaskProposalStates.Confirmed;
            ((JsonObject)value)["UpdatedAt"] = timestamp;
            ((JsonObject)value)["HumanApprovedAt"] = timestamp;
            note["Status"] = ReportStatuses.Resolved;
            note["UpdatedAt"] = timestamp;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return task;
        }
        throw new InvalidOperationException("Project remained contended; task was not confirmed.");
    }

    public async Task<FirebaseTaskProposal> ConfirmRejectionAsync(string reportId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        var projectId = ParseProjectId(reportId);
        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var note = GetNote(root, reportId, projectId);
            var proposals = GetCollection(root, "TaskProposals");
            if (!Active(note) || !proposals.TryGetPropertyValue(reportId, out var value))
                throw new InvalidOperationException("Only an active note with a ready rejection recommendation can be confirmed.");
            var proposal = ReadProposal(value, reportId, projectId);
            if (proposal.State != TaskProposalStates.Ready || proposal.Relevance != TaskProposalRelevances.Obsolete ||
                proposal.RecommendedAction != TaskProposalRecommendedActions.Reject)
                throw new InvalidOperationException("Only a ready obsolete rejection recommendation can be confirmed.");

            var timestamp = Timestamp();
            var rejected = ((JsonObject)value!).DeepClone().AsObject();
            rejected["State"] = TaskProposalStates.Rejected;
            rejected["HumanRejectedAt"] = timestamp;
            rejected["HumanApprovedAt"] = null;
            rejected["CurrentConsequences"] = null;
            rejected["UpdatedAt"] = timestamp;
            proposals[reportId] = rejected;
            root["TaskProposals"] = proposals;
            note["Status"] = ReportStatuses.Dismissed;
            note["UpdatedAt"] = timestamp;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return ReadProposal(rejected, reportId, projectId);
        }
        throw new InvalidOperationException("Project remained contended; rejection was not confirmed.");
    }

    public async Task<FirebaseTaskProposal?> GetProposalAsync(string reportId, CancellationToken cancellationToken = default)
    {
        var projectId = ParseProjectId(reportId);
        var root = ReadProject((await transport.GetAsync(ProjectPath(projectId), cancellationToken)).Json, projectId);
        GetNote(root, reportId, projectId);
        var proposals = GetCollection(root, "TaskProposals");
        return proposals.TryGetPropertyValue(reportId, out var value) ? ReadProposal(value, reportId, projectId) : null;
    }

    public async Task<BoyScoutTask?> GetTaskAsync(string reportId, CancellationToken cancellationToken = default)
    {
        var projectId = ParseProjectId(reportId);
        var root = ReadProject((await transport.GetAsync(ProjectPath(projectId), cancellationToken)).Json, projectId);
        GetNote(root, reportId, projectId);
        var tasks = GetCollection(root, "Tasks");
        var id = TaskId(reportId);
        if (!tasks.TryGetPropertyValue(id, out var value)) return null;
        if (value is not JsonObject || value.Deserialize<BoyScoutTask>() is not { } task ||
            task.Id != id || task.ProjectId != projectId || !TaskStatuses.IsValid(task.Status) ||
            task.SourceReportIds is null || !task.SourceReportIds.Contains(reportId))
            throw InvalidData();
        return task;
    }

    public async Task<BoyScoutTask?> UpdateTaskStatusAsync(string taskId, string status,
        CancellationToken cancellationToken = default)
    {
        if (!TaskStatuses.IsValid(status)) throw new ArgumentException($"Unsupported task status: {status}.", nameof(status));
        if (taskId is null || !taskId.StartsWith("BST-", StringComparison.Ordinal))
            throw new ArgumentException("Invalid Firebase task ID.", nameof(taskId));
        var reportId = taskId[4..];
        var projectId = ParseProjectId(reportId);
        if (TaskId(reportId) != taskId) throw new ArgumentException("Invalid Firebase task ID.", nameof(taskId));
        var path = ProjectPath(projectId);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var snapshot = await transport.GetAsync(path, cancellationToken);
            var root = ReadProject(snapshot.Json, projectId);
            var tasks = GetCollection(root, "Tasks");
            if (!tasks.TryGetPropertyValue(taskId, out var value)) return null;
            if (value is not JsonObject node || !HasText(node, "Id", taskId) || !HasLong(node, "ProjectId", projectId) ||
                !HasText(node, "Title") || !HasText(node, "Description") || !HasText(node, "AcceptanceCriteria") ||
                !HasText(node, "Status") || !TaskStatuses.IsValid((string)node["Status"]!) ||
                !HasText(node, "CreatedAt") || !HasText(node, "UpdatedAt") ||
                node["SourceReportIds"] is not JsonArray sources || sources.Count == 0)
                throw InvalidData();
            BoyScoutTask task;
            try { task = node.Deserialize<BoyScoutTask>() ?? throw InvalidData(); }
            catch (JsonException) { throw InvalidData(); }
            try
            {
                if (task.SourceReportIds is null || !task.SourceReportIds.Contains(reportId) ||
                    task.SourceReportIds.Any(sourceId => ParseProjectId(sourceId) != projectId)) throw InvalidData();
            }
            catch (ArgumentException) { throw InvalidData(); }

            var updatedAt = Timestamp();
            node["Status"] = status;
            node["UpdatedAt"] = updatedAt;
            root["Tasks"] = tasks;
            if (await transport.PutIfMatchAsync(path, root.ToJsonString(), snapshot.ETag, cancellationToken))
                return task with { Status = status, UpdatedAt = updatedAt };
        }
        throw new InvalidOperationException("Project remained contended; task status was not updated.");
    }

    // Project-scoped key paging only; status/search filters and a total count require a different index.
    public async Task<FirebaseTaskPage> BrowseTasksAsync(long projectId, string? before, int limit,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0) throw new ArgumentOutOfRangeException(nameof(projectId));
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (before is not null)
        {
            if (!before.StartsWith("BST-", StringComparison.Ordinal))
                throw new ArgumentException("Invalid task cursor.", nameof(before));
            try
            {
                if (ParseProjectId(before[4..]) != projectId)
                    throw new ArgumentException("Cursor belongs to a different project.", nameof(before));
            }
            catch (ArgumentException exception) when (exception.ParamName == "id")
            {
                throw new ArgumentException("Invalid task cursor.", nameof(before), exception);
            }
        }

        var json = await transport.GetKeyPageAsync(ProjectPath(projectId) + "/Tasks", before,
            limit + (before is null ? 1 : 2), cancellationToken);
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(json); }
        catch (JsonException) { throw InvalidData(); }
        if (parsed is null) return new FirebaseTaskPage([], null);
        if (parsed is not JsonObject tasks) throw InvalidData();

        var descending = new List<BoyScoutTask>();
        foreach (var entry in tasks)
        {
            var id = entry.Key;
            if (!id.StartsWith("BST-", StringComparison.Ordinal) || entry.Value is not JsonObject node ||
                !HasText(node, "Id", id) || !HasLong(node, "ProjectId", projectId) ||
                !HasText(node, "Title") || !HasText(node, "Description") ||
                !HasText(node, "AcceptanceCriteria") || !HasText(node, "Status") ||
                !HasText(node, "CreatedAt") || !HasText(node, "UpdatedAt") ||
                node["SourceReportIds"] is not JsonArray sources || sources.Count == 0)
                throw InvalidData();
            try
            {
                if (ParseProjectId(id[4..]) != projectId ||
                    sources.Any(source => source is not JsonValue value || !value.TryGetValue<string>(out var reportId) ||
                        ParseProjectId(reportId) != projectId) ||
                    !sources.Any(source => (string?)source == id[4..])) throw InvalidData();
            }
            catch (ArgumentException) { throw InvalidData(); }
            BoyScoutTask task;
            try { task = node.Deserialize<BoyScoutTask>() ?? throw InvalidData(); }
            catch (JsonException) { throw InvalidData(); }
            if (!TaskStatuses.IsValid(task.Status)) throw InvalidData();
            if (before is null || string.CompareOrdinal(id, before) < 0) descending.Add(task);
        }

        var ordered = descending.OrderByDescending(task => task.Id, StringComparer.Ordinal).ToArray();
        var items = ordered.Take(limit).ToArray();
        return new FirebaseTaskPage(items, ordered.Length > limit ? items[^1].Id : null);
    }

    private static JsonObject ReadProject(string json, long projectId)
    {
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(json); }
        catch (JsonException) { throw InvalidData(); }
        if (parsed is not JsonObject root || root["Project"] is not JsonObject project ||
            !HasLong(project, "Id", projectId) || !HasText(project, "Name") ||
            root["Notes"] is not JsonObject) throw InvalidData();
        // Validate collection shapes before a root PUT, including data we are not modifying.
        foreach (var name in new[] { "TaskProposals", "Tasks" })
            if (root.ContainsKey(name) && root[name] is not JsonObject) throw InvalidData();
        foreach (var entry in (JsonObject)root["Notes"]!)
            if (entry.Value is not JsonObject note || !HasText(note, "Id", entry.Key) ||
                !HasLong(note, "ProjectId", projectId) || !HasText(note, "Status") ||
                !ReportStatuses.IsValid((string)note["Status"]!)) throw InvalidData();
        return root;
    }

    private static JsonObject GetNote(JsonObject root, string id, long projectId)
    {
        if (((JsonObject)root["Notes"]!).TryGetPropertyValue(id, out var value) && value is JsonObject note &&
            HasText(note, "Id", id) && HasLong(note, "ProjectId", projectId)) return note;
        throw new KeyNotFoundException($"Remote note {id} was not found.");
    }

    private static JsonObject GetCollection(JsonObject root, string name) => root[name] as JsonObject ?? new JsonObject();

    private static bool HasLinkedTask(JsonObject tasks, string reportId)
    {
        foreach (var entry in tasks)
        {
            if (entry.Value is not JsonObject task || task["SourceReportIds"] is not JsonArray ids ||
                ids.Any(id => id is not JsonValue value || !value.TryGetValue<string>(out _)))
                throw InvalidData();
            if (ids.Any(id => (string?)id == reportId)) return true;
        }
        return false;
    }

    private static FirebaseTaskProposal ReadProposal(JsonNode? value, string id, long projectId)
    {
        if (value is not JsonObject node || !HasText(node, "ReportId", id) ||
            !HasLong(node, "ProjectId", projectId) || !HasText(node, "State") ||
            !HasText(node, "CreatedAt") || !HasText(node, "UpdatedAt") ||
            !OptionalText(node, "Relevance") || !OptionalText(node, "RecommendedAction") ||
            !OptionalText(node, "Title") || !OptionalText(node, "Description") ||
            !OptionalText(node, "AcceptanceCriteria") || !OptionalText(node, "VerificationSummary") ||
            !OptionalText(node, "CurrentConsequences") || !OptionalText(node, "FinalRejectionReason") ||
            !OptionalText(node, "Error") || !OptionalText(node, "ReviewIntent") ||
            !OptionalText(node, "RequestedRejectionReason") || !OptionalText(node, "RequestedClarification") ||
            !OptionalText(node, "LocalizedReportTitle") || !OptionalText(node, "LocalizedReportObservation") ||
            !OptionalText(node, "LocalizedReportImpact") || !OptionalText(node, "LocalizedReportSuggestedAction") ||
            node.ContainsKey("Attempts") && !TryReadAttempts(node, out _)) throw InvalidData();
        var proposal = node.Deserialize<FirebaseTaskProposal>()!;
        if (proposal.Attempts < 0) throw InvalidData();
        var current = proposal.Relevance == TaskProposalRelevances.Current &&
            proposal.RecommendedAction == TaskProposalRecommendedActions.CreateTask &&
            HasText(node, "Title") && HasText(node, "Description") && HasText(node, "AcceptanceCriteria") &&
            proposal.State is TaskProposalStates.Ready or TaskProposalStates.Confirmed;
        var obsolete = proposal.State is TaskProposalStates.Ready or TaskProposalStates.Rejected &&
            proposal.Relevance == TaskProposalRelevances.Obsolete &&
            proposal.RecommendedAction == TaskProposalRecommendedActions.Reject &&
            HasText(node, "FinalRejectionReason") && HasText(node, "VerificationSummary") &&
            proposal.Title is null && proposal.Description is null && proposal.AcceptanceCriteria is null;
        var unverified = proposal.State == TaskProposalStates.Unverified &&
            proposal.Relevance is TaskProposalRelevances.Current or TaskProposalRelevances.Unverified &&
            proposal.RecommendedAction is null && HasText(node, "VerificationSummary");
        var failed = proposal.State == TaskProposalStates.Failed && proposal.RecommendedAction is null && HasText(node, "Error");
        var processing = proposal.State == TaskProposalStates.Processing &&
            proposal.Relevance is null && proposal.RecommendedAction is null && proposal.Title is null &&
            proposal.Description is null && proposal.AcceptanceCriteria is null &&
            proposal.VerificationSummary is null &&
            proposal.FinalRejectionReason is null && proposal.Error is null;
        var queued = proposal.State == TaskProposalStates.Queued &&
            proposal.Relevance is null && proposal.RecommendedAction is null && proposal.Title is null &&
            proposal.Description is null && proposal.AcceptanceCriteria is null &&
            proposal.VerificationSummary is null &&
            proposal.FinalRejectionReason is null && proposal.Error is null;
        if (!(current || obsolete || unverified || failed || processing || queued)) throw InvalidData();
        return proposal;
    }

    private static bool OptionalText(JsonObject node, string key) =>
        !node.ContainsKey(key) || node[key] is null || HasText(node, key);

    private static int ReadAttempts(JsonObject node) => TryReadAttempts(node, out var attempts) ? attempts : 0;

    private static bool TryReadAttempts(JsonObject node, out int attempts)
    {
        attempts = 0;
        return !node.ContainsKey("Attempts") || node["Attempts"] is JsonValue value && value.TryGetValue(out attempts) && attempts >= 0;
    }

    private static bool HasText(JsonObject obj, string key, string? expected = null) =>
        obj[key] is JsonValue value && value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text) && (expected is null || text == expected);

    private static bool HasLong(JsonObject obj, string key, long expected) =>
        obj[key] is JsonValue value && value.TryGetValue<long>(out var number) && number == expected;

    private static bool Active(JsonObject note) => (string)note["Status"]! is
        ReportStatuses.Open or ReportStatuses.Accepted or ReportStatuses.Planned;

    private static void Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", name);
    }

    private static void OptionalRequired(string? value, string name)
    {
        if (value is not null) Required(value, name);
    }

    private static void ValidateLocalized(params string?[] values)
    {
        foreach (var value in values) OptionalRequired(value, "localizedReport");
    }

    private static string? Trim(string? value) => value?.Trim();

    private static long ParseProjectId(string id)
    {
        if (id is null) throw new ArgumentException("Invalid remote note ID.", nameof(id));
        var parts = id.Split('-');
        if (parts.Length != 3 || parts[0] != "BSR" ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var projectId) || projectId <= 0 ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0 ||
            id != $"BSR-{projectId.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString("D19", CultureInfo.InvariantCulture)}")
            throw new ArgumentException("Invalid remote note ID.", nameof(id));
        return projectId;
    }

    private static string ProjectPath(long id) => "report-projects/" + id.ToString(CultureInfo.InvariantCulture);
    private static string TaskId(string reportId) => "BST-" + reportId;
    private static string Timestamp() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static InvalidOperationException InvalidData() => new("Remote project data is invalid; refusing to overwrite it.");
}
