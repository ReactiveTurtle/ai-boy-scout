using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoyScout.Core.Reports;
using BoyScout.Core.Tasks;

namespace BoyScout.Core.Storage.Firebase;

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

// Standalone workflow: neither task creation nor API integration happens without explicit confirmation.
public sealed class FirebaseTaskRepository(FirebaseRealtimeDatabaseTransport transport)
{
    private const int MaxAttempts = 5;

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
            proposal.VerificationSummary is null && proposal.CurrentConsequences is null &&
            proposal.FinalRejectionReason is null && proposal.Error is null;
        if (!(current || obsolete || unverified || failed || processing)) throw InvalidData();
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
