using System.Globalization;
using BoyScout.Core.Reports;
using BoyScout.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BoyScout.Core.Tasks;

public sealed class TaskStore(BoyScoutDatabase database)
{
    public const int MaximumRejectionReasonLength = 2_000;
    public const int MaximumClarificationLength = 2_000;

    public TaskProposal? TryClaimNextProposal()
    {
        var timestamp = UtcTimestamp();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using (var blockerCommand = connection.CreateCommand())
        {
            blockerCommand.Transaction = transaction;
            blockerCommand.CommandText =
                """
                SELECT EXISTS (
                  SELECT 1
                  FROM task_proposals
                  JOIN project_ai_access
                    ON project_ai_access.project_id = task_proposals.project_id
                  WHERE task_proposals.state = 'processing'
                );
                """;
            if (Convert.ToBoolean(blockerCommand.ExecuteScalar(), CultureInfo.InvariantCulture))
            {
                transaction.Commit();
                return null;
            }
        }

        object? result;
        using (var queuedCommand = connection.CreateCommand())
        {
            queuedCommand.Transaction = transaction;
            queuedCommand.CommandText =
                """
                UPDATE task_proposals
                SET state = 'processing', updated_at = $updatedAt
                WHERE id = (
                  SELECT task_proposals.id
                  FROM task_proposals
                  JOIN reports ON reports.sequence = task_proposals.report_sequence
                  JOIN project_ai_access
                    ON project_ai_access.project_id = task_proposals.project_id
                  WHERE task_proposals.state = 'queued'
                    AND reports.status IN ('open', 'accepted', 'planned')
                    AND NOT EXISTS (
                      SELECT 1 FROM report_tasks
                      WHERE report_tasks.report_sequence = task_proposals.report_sequence
                    )
                  ORDER BY task_proposals.updated_at, task_proposals.id
                  LIMIT 1
                )
                RETURNING id;
                """;
            queuedCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            result = queuedCommand.ExecuteScalar();
        }

        if (result is null)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO task_proposals (
                  report_sequence, project_id, state, attempts, created_at, updated_at
                )
                SELECT reports.sequence, reports.project_id, 'processing', 1, $createdAt, $updatedAt
                FROM reports
                WHERE reports.status IN ('open', 'accepted', 'planned')
                  AND EXISTS (
                    SELECT 1 FROM project_ai_access
                    WHERE project_ai_access.project_id = reports.project_id
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM task_proposals
                    WHERE task_proposals.report_sequence = reports.sequence
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM report_tasks WHERE report_tasks.report_sequence = reports.sequence
                  )
                ORDER BY reports.sequence
                LIMIT 1
                RETURNING id;
                """;
            insertCommand.Parameters.AddWithValue("$createdAt", timestamp);
            insertCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            result = insertCommand.ExecuteScalar();
        }

        if (result is null)
        {
            transaction.Commit();
            return null;
        }

        var proposalId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        var proposal = GetProposal(connection, transaction, proposalId)
            ?? throw new InvalidOperationException("The claimed task proposal could not be read back.");
        transaction.Commit();
        return proposal;
    }

    public TaskProposal SaveProposal(
        long id,
        string title,
        string description,
        string acceptanceCriteria,
        string? verificationSummary = null,
        string? localizedReportTitle = null,
        string? localizedReportObservation = null,
        string? localizedReportImpact = null,
        string? localizedReportSuggestedAction = null)
    {
        ValidatePositive(id, nameof(id));
        ValidateRequired(title, nameof(title));
        ValidateRequired(description, nameof(description));
        ValidateRequired(acceptanceCriteria, nameof(acceptanceCriteria));
        if (verificationSummary is not null)
        {
            ValidateRequired(verificationSummary, nameof(verificationSummary));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'ready', relevance = 'current', recommended_action = 'create_task',
                final_rejection_reason = NULL, verification_summary = $verificationSummary,
                title = $title, description = $description,
                acceptance_criteria = $acceptanceCriteria,
                localized_report_title = $localizedReportTitle,
                localized_report_observation = $localizedReportObservation,
                localized_report_impact = $localizedReportImpact,
                localized_report_suggested_action = $localizedReportSuggestedAction,
                error = NULL, updated_at = $updatedAt
            WHERE id = $id AND state = 'processing';
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$acceptanceCriteria", acceptanceCriteria);
        command.Parameters.AddWithValue("$verificationSummary", DbValue(verificationSummary));
        command.Parameters.AddWithValue("$localizedReportTitle", DbValue(localizedReportTitle));
        command.Parameters.AddWithValue("$localizedReportObservation", DbValue(localizedReportObservation));
        command.Parameters.AddWithValue("$localizedReportImpact", DbValue(localizedReportImpact));
        command.Parameters.AddWithValue("$localizedReportSuggestedAction", DbValue(localizedReportSuggestedAction));
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$id", id);
        EnsureSingleTransition(command, "Only a processing proposal can be saved.");
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The saved task proposal could not be read back.");
    }

    public TaskProposal FailProposal(long id, string error)
    {
        ValidatePositive(id, nameof(id));
        ValidateRequired(error, nameof(error));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'failed', error = $error, updated_at = $updatedAt
            WHERE id = $id AND state = 'processing';
            """;
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$id", id);
        EnsureSingleTransition(command, "Only a processing proposal can be failed.");
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The failed task proposal could not be read back.");
    }

    public TaskProposal MarkUnverified(
        long id,
        string summary,
        string relevance = TaskProposalRelevances.Unverified,
        string? localizedReportTitle = null,
        string? localizedReportObservation = null,
        string? localizedReportImpact = null,
        string? localizedReportSuggestedAction = null)
    {
        ValidatePositive(id, nameof(id));
        ValidateRequired(summary, nameof(summary));
        if (relevance is not (TaskProposalRelevances.Current or TaskProposalRelevances.Unverified))
        {
            throw new ArgumentException(
                "An unverified proposal must have current or unverified relevance.",
                nameof(relevance));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'unverified', relevance = $relevance, recommended_action = NULL,
                final_rejection_reason = NULL, verification_summary = $summary,
                title = NULL, description = NULL, acceptance_criteria = NULL,
                localized_report_title = $localizedReportTitle,
                localized_report_observation = $localizedReportObservation,
                localized_report_impact = $localizedReportImpact,
                localized_report_suggested_action = $localizedReportSuggestedAction,
                error = NULL, updated_at = $updatedAt
            WHERE id = $id AND state = 'processing';
            """;
        command.Parameters.AddWithValue("$summary", summary);
        command.Parameters.AddWithValue("$relevance", relevance);
        command.Parameters.AddWithValue("$localizedReportTitle", DbValue(localizedReportTitle));
        command.Parameters.AddWithValue("$localizedReportObservation", DbValue(localizedReportObservation));
        command.Parameters.AddWithValue("$localizedReportImpact", DbValue(localizedReportImpact));
        command.Parameters.AddWithValue("$localizedReportSuggestedAction", DbValue(localizedReportSuggestedAction));
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$id", id);
        EnsureSingleTransition(command, "Only a processing proposal can be marked unverified.");
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The unverified task proposal could not be read back.");
    }

    public TaskProposal SaveRejectionRecommendation(
        long id,
        string finalRejectionReason,
        string verificationSummary,
        string? localizedReportTitle = null,
        string? localizedReportObservation = null,
        string? localizedReportImpact = null,
        string? localizedReportSuggestedAction = null)
    {
        ValidatePositive(id, nameof(id));
        ValidateRequired(finalRejectionReason, nameof(finalRejectionReason));
        ValidateRequired(verificationSummary, nameof(verificationSummary));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'ready', relevance = 'obsolete', recommended_action = 'reject',
                final_rejection_reason = $finalRejectionReason,
                verification_summary = $verificationSummary,
                title = NULL, description = NULL, acceptance_criteria = NULL,
                localized_report_title = $localizedReportTitle,
                localized_report_observation = $localizedReportObservation,
                localized_report_impact = $localizedReportImpact,
                localized_report_suggested_action = $localizedReportSuggestedAction,
                error = NULL, updated_at = $updatedAt
            WHERE id = $id AND state = 'processing';
            """;
        command.Parameters.AddWithValue("$finalRejectionReason", finalRejectionReason);
        command.Parameters.AddWithValue("$verificationSummary", verificationSummary);
        command.Parameters.AddWithValue("$localizedReportTitle", DbValue(localizedReportTitle));
        command.Parameters.AddWithValue("$localizedReportObservation", DbValue(localizedReportObservation));
        command.Parameters.AddWithValue("$localizedReportImpact", DbValue(localizedReportImpact));
        command.Parameters.AddWithValue("$localizedReportSuggestedAction", DbValue(localizedReportSuggestedAction));
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$id", id);
        EnsureSingleTransition(command, "Only a processing proposal can receive a rejection recommendation.");
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The rejection recommendation could not be read back.");
    }

    public TaskProposal RetryProposal(long id)
    {
        ValidatePositive(id, nameof(id));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'queued', title = NULL, description = NULL,
                acceptance_criteria = NULL, relevance = NULL, verification_summary = NULL,
                recommended_action = NULL, final_rejection_reason = NULL,
                error = NULL,
                attempts = attempts + 1, updated_at = $updatedAt
            WHERE id = $id AND state IN ('failed', 'ready', 'unverified');
            """;
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$id", id);
        EnsureSingleTransition(command, "Only a failed, ready, or unverified proposal can be retried.");
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The retried task proposal could not be read back.");
    }

    public TaskProposal RequeueProcessingProposal(long id)
    {
        ValidatePositive(id, nameof(id));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'queued', updated_at = $updatedAt
            WHERE id = $id AND state = 'processing';
            """;
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$id", id);
        EnsureSingleTransition(command, "Only a processing proposal can be requeued.");
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The requeued proposal could not be read back.");
    }

    public TaskProposal RequestRejectionReview(string reportId, string reason)
    {
        var reportSequence = ParseReportId(reportId);
        ValidateRequired(reason, nameof(reason));
        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > MaximumRejectionReasonLength)
        {
            throw new ArgumentException(
                $"Rejection reason must not exceed {MaximumRejectionReasonLength} characters.",
                nameof(reason));
        }

        var timestamp = UtcTimestamp();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        string reportStatus;
        long projectId;
        bool hasAiAccess;
        long? proposalId;
        string? proposalState;
        using (var stateCommand = connection.CreateCommand())
        {
            stateCommand.Transaction = transaction;
            stateCommand.CommandText =
                """
                SELECT reports.status, reports.project_id,
                       project_ai_access.project_id IS NOT NULL,
                       task_proposals.id, task_proposals.state
                FROM reports
                LEFT JOIN project_ai_access ON project_ai_access.project_id = reports.project_id
                LEFT JOIN task_proposals ON task_proposals.report_sequence = reports.sequence
                WHERE reports.sequence = $reportSequence;
                """;
            stateCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            using var reader = stateCommand.ExecuteReader();
            if (!reader.Read())
            {
                throw new KeyNotFoundException($"Boy Scout report {reportId} was not found.");
            }

            reportStatus = reader.GetString(0);
            projectId = reader.GetInt64(1);
            hasAiAccess = reader.GetBoolean(2);
            proposalId = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            proposalState = reader.IsDBNull(4) ? null : reader.GetString(4);
        }

        if (reportStatus is not (
            ReportStatuses.Open or ReportStatuses.Accepted or ReportStatuses.Planned))
        {
            throw new InvalidOperationException("Only an active report can be reviewed for rejection.");
        }

        if (!hasAiAccess)
        {
            throw new InvalidOperationException(
                "AI read access must be granted for the report project before rejection review.");
        }

        if (proposalState is TaskProposalStates.Queued or TaskProposalStates.Processing)
        {
            throw new InvalidOperationException("A review is already queued or processing for this report.");
        }

        if (proposalState is TaskProposalStates.Confirmed or TaskProposalStates.Rejected)
        {
            throw new InvalidOperationException("A completed proposal cannot be reviewed for rejection.");
        }

        long id;
        using (var writeCommand = connection.CreateCommand())
        {
            writeCommand.Transaction = transaction;
            writeCommand.Parameters.AddWithValue("$reason", normalizedReason);
            writeCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            if (proposalId is null)
            {
                writeCommand.CommandText =
                    """
                    INSERT INTO task_proposals (
                      report_sequence, project_id, state, review_intent,
                      requested_rejection_reason, requested_clarification,
                      attempts, created_at, updated_at
                    ) VALUES (
                      $reportSequence, $projectId, 'queued', 'rejection',
                      $reason, NULL, 1, $createdAt, $updatedAt
                    )
                    RETURNING id;
                    """;
                writeCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
                writeCommand.Parameters.AddWithValue("$projectId", projectId);
                writeCommand.Parameters.AddWithValue("$createdAt", timestamp);
                id = Convert.ToInt64(writeCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            else
            {
                writeCommand.CommandText =
                    """
                    UPDATE task_proposals
                    SET state = 'queued', review_intent = 'rejection',
                        requested_rejection_reason = $reason, requested_clarification = NULL,
                        relevance = NULL, verification_summary = NULL,
                        recommended_action = NULL, final_rejection_reason = NULL,
                        title = NULL, description = NULL, acceptance_criteria = NULL,
                        error = NULL, attempts = attempts + 1, updated_at = $updatedAt
                    WHERE id = $id AND state IN ('ready', 'unverified', 'failed');
                    """;
                writeCommand.Parameters.AddWithValue("$id", proposalId.Value);
                EnsureSingleTransition(
                    writeCommand,
                    "Only a ready, unverified, or failed proposal can be resubmitted for rejection review.");
                id = proposalId.Value;
            }
        }

        var proposal = GetProposal(connection, transaction, id)
            ?? throw new InvalidOperationException("The rejection review could not be read back.");
        transaction.Commit();
        return proposal;
    }

    public TaskProposal RequestClarification(string reportId, string instruction)
    {
        var reportSequence = ParseReportId(reportId);
        ValidateRequired(instruction, nameof(instruction));
        var normalizedInstruction = instruction.Trim();
        if (normalizedInstruction.Length > MaximumClarificationLength)
        {
            throw new ArgumentException(
                $"Clarification instruction must not exceed {MaximumClarificationLength} characters.",
                nameof(instruction));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE task_proposals
            SET state = 'queued', review_intent = 'proposal',
                requested_rejection_reason = NULL, requested_clarification = $instruction,
                relevance = NULL, verification_summary = NULL,
                recommended_action = NULL, final_rejection_reason = NULL,
                title = NULL, description = NULL, acceptance_criteria = NULL,
                error = NULL, attempts = attempts + 1, updated_at = $updatedAt
            WHERE report_sequence = $reportSequence
              AND state IN ('ready', 'unverified', 'failed')
              AND EXISTS (
                SELECT 1 FROM reports
                WHERE reports.sequence = task_proposals.report_sequence
                  AND reports.status IN ('open', 'accepted', 'planned')
              )
              AND EXISTS (
                SELECT 1 FROM project_ai_access
                WHERE project_ai_access.project_id = task_proposals.project_id
              )
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$instruction", normalizedInstruction);
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$reportSequence", reportSequence);
        var result = command.ExecuteScalar();
        if (result is null)
        {
            using var stateCommand = connection.CreateCommand();
            stateCommand.CommandText =
                """
                SELECT reports.status, task_proposals.id, task_proposals.state,
                       project_ai_access.project_id IS NOT NULL
                FROM reports
                LEFT JOIN task_proposals ON task_proposals.report_sequence = reports.sequence
                LEFT JOIN project_ai_access ON project_ai_access.project_id = reports.project_id
                WHERE reports.sequence = $reportSequence;
                """;
            stateCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            using var reader = stateCommand.ExecuteReader();
            if (!reader.Read())
            {
                throw new KeyNotFoundException($"Boy Scout report {reportId} was not found.");
            }

            if (reader.GetString(0) is not (
                ReportStatuses.Open or ReportStatuses.Accepted or ReportStatuses.Planned))
            {
                throw new InvalidOperationException("Only an active report can request clarification.");
            }

            if (!reader.GetBoolean(3))
            {
                throw new InvalidOperationException(
                    "AI read access must be granted for the report project before clarification.");
            }

            if (reader.IsDBNull(1))
            {
                throw new InvalidOperationException("A proposal must exist before clarification can be requested.");
            }

            throw new InvalidOperationException(
                "Only a ready, unverified, or failed proposal can request clarification.");
        }

        var id = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        return GetProposal(connection, transaction: null, id)
            ?? throw new InvalidOperationException("The clarification request could not be read back.");
    }

    public IReadOnlyList<TaskProposal> ListReadyProposals()
    {
        using var connection = database.OpenConnection();
        using var command = CreateProposalSelect(connection, transaction: null);
        command.CommandText += " WHERE task_proposals.state = 'ready' ORDER BY task_proposals.id;";
        return ReadProposals(command);
    }

    public IReadOnlyList<TaskProposal> ListActiveProposals()
    {
        using var connection = database.OpenConnection();
        using var command = CreateProposalSelect(connection, transaction: null);
        command.CommandText +=
            " WHERE task_proposals.state IN ('queued', 'processing', 'ready', 'failed', 'unverified')"
            + " ORDER BY task_proposals.id;";
        return ReadProposals(command);
    }

    public TaskProposal? GetProcessingProposal()
    {
        using var connection = database.OpenConnection();
        using var command = CreateProposalSelect(connection, transaction: null);
        command.CommandText +=
            " WHERE task_proposals.state = 'processing'"
            + " AND EXISTS (SELECT 1 FROM project_ai_access"
            + " WHERE project_ai_access.project_id = task_proposals.project_id)"
            + " ORDER BY task_proposals.id LIMIT 1;";
        return ReadProposals(command).FirstOrDefault();
    }

    public TaskProposal? GetCurrentProposal()
    {
        using var connection = database.OpenConnection();
        using var command = CreateProposalSelect(connection, transaction: null);
        command.CommandText +=
            " WHERE task_proposals.state IN ('processing', 'ready', 'queued', 'unverified', 'failed')"
            + " AND EXISTS (SELECT 1 FROM project_ai_access"
            + " WHERE project_ai_access.project_id = task_proposals.project_id)"
            + " ORDER BY CASE task_proposals.state"
            + " WHEN 'processing' THEN 0 WHEN 'ready' THEN 1 WHEN 'queued' THEN 2"
            + " WHEN 'unverified' THEN 3 WHEN 'failed' THEN 4 END,"
            + " task_proposals.id LIMIT 1;";
        return ReadProposals(command).FirstOrDefault();
    }

    public BoyScoutTask ConfirmProposal(long id)
    {
        ValidatePositive(id, nameof(id));
        var timestamp = UtcTimestamp();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        var proposal = GetProposal(connection, transaction, id);
        if (proposal is null ||
            proposal.State != TaskProposalStates.Ready ||
            proposal.Relevance != TaskProposalRelevances.Current ||
            proposal.RecommendedAction != TaskProposalRecommendedActions.CreateTask)
        {
            throw new InvalidOperationException("Only a ready create-task recommendation can be confirmed.");
        }

        long taskSequence;
        using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO tasks (
                  project_id, title, description, acceptance_criteria, status, created_at, updated_at
                ) VALUES (
                  $projectId, $title, $description, $acceptanceCriteria, 'todo', $createdAt, $updatedAt
                )
                RETURNING sequence;
                """;
            insertCommand.Parameters.AddWithValue("$projectId", proposal.ProjectId);
            insertCommand.Parameters.AddWithValue("$title", proposal.Title!);
            insertCommand.Parameters.AddWithValue("$description", proposal.Description!);
            insertCommand.Parameters.AddWithValue("$acceptanceCriteria", proposal.AcceptanceCriteria!);
            insertCommand.Parameters.AddWithValue("$createdAt", timestamp);
            insertCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            taskSequence = Convert.ToInt64(insertCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var linkCommand = connection.CreateCommand())
        {
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                """
                INSERT INTO report_tasks (report_sequence, task_sequence)
                VALUES ($reportSequence, $taskSequence);
                """;
            linkCommand.Parameters.AddWithValue("$reportSequence", ParseReportId(proposal.ReportId));
            linkCommand.Parameters.AddWithValue("$taskSequence", taskSequence);
            linkCommand.ExecuteNonQuery();
        }

        using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                """
                UPDATE task_proposals
                SET state = 'confirmed', updated_at = $updatedAt
                WHERE id = $id AND state = 'ready' AND relevance = 'current';

                UPDATE reports
                SET status = 'resolved', updated_at = $updatedAt
                WHERE sequence = $reportSequence;
                """;
            updateCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            updateCommand.Parameters.AddWithValue("$id", id);
            updateCommand.Parameters.AddWithValue("$reportSequence", ParseReportId(proposal.ReportId));
            updateCommand.ExecuteNonQuery();
        }

        var task = GetTask(connection, transaction, taskSequence)
            ?? throw new InvalidOperationException("The confirmed task could not be read back.");
        transaction.Commit();
        return task;
    }

    public TaskProposal ConfirmRejection(long id)
    {
        ValidatePositive(id, nameof(id));
        var timestamp = UtcTimestamp();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        var proposal = GetProposal(connection, transaction, id);
        if (proposal is null ||
            proposal.State != TaskProposalStates.Ready ||
            proposal.Relevance != TaskProposalRelevances.Obsolete ||
            proposal.RecommendedAction != TaskProposalRecommendedActions.Reject)
        {
            throw new InvalidOperationException("Only a ready rejection recommendation can be confirmed.");
        }

        using (var proposalCommand = connection.CreateCommand())
        {
            proposalCommand.Transaction = transaction;
            proposalCommand.CommandText =
                """
                UPDATE task_proposals
                SET state = 'rejected', updated_at = $updatedAt
                WHERE id = $id AND state = 'ready' AND relevance = 'obsolete'
                  AND recommended_action = 'reject';
                """;
            proposalCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            proposalCommand.Parameters.AddWithValue("$id", id);
            EnsureSingleTransition(proposalCommand, "The rejection recommendation changed before confirmation.");
        }

        using (var reportCommand = connection.CreateCommand())
        {
            reportCommand.Transaction = transaction;
            reportCommand.CommandText =
                """
                UPDATE reports
                SET status = 'dismissed', updated_at = $updatedAt
                WHERE sequence = $reportSequence
                  AND status IN ('open', 'accepted', 'planned');
                """;
            reportCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            reportCommand.Parameters.AddWithValue("$reportSequence", ParseReportId(proposal.ReportId));
            EnsureSingleTransition(reportCommand, "Only an active report can have rejection confirmed.");
        }

        var rejected = GetProposal(connection, transaction, id)
            ?? throw new InvalidOperationException("The rejected proposal could not be read back.");
        transaction.Commit();
        return rejected;
    }

    public IReadOnlyList<BoyScoutTask> ListTasks(long? projectId = null, string? status = null)
    {
        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "Project ID must be positive.");
        }

        if (status is not null && !TaskStatuses.IsValid(status))
        {
            throw new ArgumentException($"Unsupported task status: {status}.", nameof(status));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var conditions = new List<string>();
        if (projectId is not null)
        {
            conditions.Add("project_id = $projectId");
            command.Parameters.AddWithValue("$projectId", projectId.Value);
        }

        if (status is not null)
        {
            conditions.Add("status = $status");
            command.Parameters.AddWithValue("$status", status);
        }

        var whereClause = conditions.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", conditions)}";
        command.CommandText =
            $"""
            SELECT tasks.sequence, tasks.project_id, tasks.title, tasks.description,
                   tasks.acceptance_criteria, tasks.status, tasks.created_at, tasks.updated_at,
                   COALESCE((
                     SELECT group_concat(source.report_sequence, ',')
                     FROM (
                       SELECT report_sequence
                       FROM report_tasks
                       WHERE task_sequence = tasks.sequence
                       ORDER BY report_sequence
                     ) AS source
                   ), '')
            FROM tasks
            {whereClause}
            ORDER BY sequence DESC;
            """;
        return ReadTasks(command);
    }

    public NoteWorkflowInfo? GetNoteWorkflow(string reportId)
    {
        var reportSequence = ParseReportId(reportId);
        using var connection = database.OpenConnection();
        return GetNoteWorkflow(connection, transaction: null, reportSequence);
    }

    public IReadOnlyList<NoteWorkflowInfo> GetNoteWorkflows(IReadOnlyList<string> reportIds)
    {
        ArgumentNullException.ThrowIfNull(reportIds);
        if (reportIds.Count > 100)
        {
            throw new ArgumentException("At most 100 report IDs can be requested.", nameof(reportIds));
        }

        var sequences = reportIds.Select(ParseReportId).ToArray();
        if (sequences.Length == 0)
        {
            return [];
        }

        using var connection = database.OpenConnection();
        var workflows = new Dictionary<long, NoteWorkflowInfo>();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT
              reports.sequence,
              reports.status,
              task_proposals.state,
              task_proposals.relevance,
              task_proposals.recommended_action,
              task_proposals.final_rejection_reason,
              CASE
                WHEN task_proposals.final_rejection_reason IS NOT NULL
                  THEN task_proposals.final_rejection_reason
                WHEN task_proposals.verification_summary IS NOT NULL
                  THEN task_proposals.verification_summary
                WHEN task_proposals.state = 'failed' THEN task_proposals.error
                ELSE NULL
              END,
              CASE
                WHEN task_proposals.review_intent = 'rejection'
                 AND task_proposals.state IN ('ready', 'unverified', 'rejected')
                  THEN task_proposals.requested_rejection_reason
                ELSE NULL
              END,
              CASE
                WHEN (task_proposals.review_intent = 'rejection'
                      OR task_proposals.relevance = 'obsolete')
                 AND task_proposals.state IN ('ready', 'unverified', 'rejected')
                  THEN task_proposals.verification_summary
                ELSE NULL
              END,
              COALESCE(task_proposals.updated_at, reports.updated_at)
            FROM reports
            LEFT JOIN task_proposals ON task_proposals.report_sequence = reports.sequence
            WHERE reports.sequence IN ({string.Join(", ", sequences.Select((_, index) => $"$id{index}"))});
            """;
        for (var index = 0; index < sequences.Length; index++)
        {
            command.Parameters.AddWithValue($"$id{index}", sequences[index]);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            workflows.Add(reader.GetInt64(0), ReadNoteWorkflow(reader));
        }

        return sequences.Where(workflows.ContainsKey).Select(sequence => workflows[sequence]).ToArray();
    }

    public TaskProposal? GetProposalForReport(string reportId)
    {
        var reportSequence = ParseReportId(reportId);
        using var connection = database.OpenConnection();
        using (var reportCommand = connection.CreateCommand())
        {
            reportCommand.CommandText = "SELECT EXISTS (SELECT 1 FROM reports WHERE sequence = $sequence);";
            reportCommand.Parameters.AddWithValue("$sequence", reportSequence);
            if (!Convert.ToBoolean(reportCommand.ExecuteScalar(), CultureInfo.InvariantCulture))
            {
                throw new KeyNotFoundException($"Boy Scout report {reportId} was not found.");
            }
        }

        return GetProposalByReportSequence(connection, transaction: null, reportSequence);
    }

    public RestoredNote RestoreRejectedNote(string reportId)
    {
        var reportSequence = ParseReportId(reportId);
        var timestamp = UtcTimestamp();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        long projectId;
        long? proposalId;
        string reportStatus;
        string? proposalState;
        using (var stateCommand = connection.CreateCommand())
        {
            stateCommand.Transaction = transaction;
            stateCommand.CommandText =
                """
                SELECT reports.status, reports.project_id, task_proposals.id, task_proposals.state
                FROM reports
                LEFT JOIN task_proposals ON task_proposals.report_sequence = reports.sequence
                WHERE reports.sequence = $reportSequence;
                """;
            stateCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            using var reader = stateCommand.ExecuteReader();
            if (!reader.Read())
            {
                throw new KeyNotFoundException($"Boy Scout report {reportId} was not found.");
            }

            reportStatus = reader.GetString(0);
            projectId = reader.GetInt64(1);
            proposalId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            proposalState = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        if (reportStatus != ReportStatuses.Dismissed)
        {
            throw new InvalidOperationException("Only a dismissed report can be restored.");
        }

        if (proposalState is not null && proposalState != TaskProposalStates.Rejected)
        {
            throw new InvalidOperationException(
                $"A dismissed report with an existing {proposalState} proposal cannot be restored.");
        }

        using (var reportCommand = connection.CreateCommand())
        {
            reportCommand.Transaction = transaction;
            reportCommand.CommandText =
                "UPDATE reports SET status = 'open', updated_at = $updatedAt WHERE sequence = $sequence;";
            reportCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            reportCommand.Parameters.AddWithValue("$sequence", reportSequence);
            EnsureSingleTransition(reportCommand, "The rejected report could not be restored.");
        }

        if (proposalId is null)
        {
            using var proposalCommand = connection.CreateCommand();
            proposalCommand.Transaction = transaction;
            proposalCommand.CommandText =
                """
                INSERT INTO task_proposals (
                  report_sequence, project_id, state, review_intent,
                  requested_rejection_reason, requested_clarification,
                  attempts, created_at, updated_at
                ) VALUES (
                  $reportSequence, $projectId, 'queued', 'proposal', NULL, NULL,
                  0, $createdAt, $updatedAt
                )
                RETURNING id;
                """;
            proposalCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            proposalCommand.Parameters.AddWithValue("$projectId", projectId);
            proposalCommand.Parameters.AddWithValue("$createdAt", timestamp);
            proposalCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            proposalId = Convert.ToInt64(proposalCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            using var proposalCommand = connection.CreateCommand();
            proposalCommand.Transaction = transaction;
            proposalCommand.CommandText =
                """
                UPDATE task_proposals
                SET state = 'queued', review_intent = 'proposal',
                    requested_rejection_reason = NULL, requested_clarification = NULL,
                    relevance = NULL, recommended_action = NULL, final_rejection_reason = NULL,
                    verification_summary = NULL, title = NULL, description = NULL,
                    acceptance_criteria = NULL, error = NULL, updated_at = $updatedAt
                WHERE id = $id AND state = 'rejected';
                """;
            proposalCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            proposalCommand.Parameters.AddWithValue("$id", proposalId.Value);
            EnsureSingleTransition(proposalCommand, "The rejected proposal could not be restored.");
        }

        var proposal = GetProposal(connection, transaction, proposalId.Value)
            ?? throw new InvalidOperationException("The restored proposal could not be read back.");
        var workflow = GetNoteWorkflow(connection, transaction, reportSequence)
            ?? throw new InvalidOperationException("The restored workflow could not be read back.");
        transaction.Commit();
        return new RestoredNote(workflow, proposal);
    }

    public ReprocessedNote ReprocessCompletedNote(string reportId)
    {
        var reportSequence = ParseReportId(reportId);
        var timestamp = UtcTimestamp();
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        long projectId;
        long? proposalId;
        string reportStatus;
        string? proposalState;
        using (var stateCommand = connection.CreateCommand())
        {
            stateCommand.Transaction = transaction;
            stateCommand.CommandText =
                """
                SELECT reports.status, reports.project_id, task_proposals.id, task_proposals.state
                FROM reports
                LEFT JOIN task_proposals ON task_proposals.report_sequence = reports.sequence
                WHERE reports.sequence = $reportSequence;
                """;
            stateCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            using var reader = stateCommand.ExecuteReader();
            if (!reader.Read())
            {
                throw new KeyNotFoundException($"Boy Scout report {reportId} was not found.");
            }

            reportStatus = reader.GetString(0);
            projectId = reader.GetInt64(1);
            proposalId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            proposalState = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        if (reportStatus != ReportStatuses.Resolved)
        {
            throw new InvalidOperationException("Only a resolved report can be reprocessed.");
        }

        var linkedTasks = new List<long>();
        using (var linkCommand = connection.CreateCommand())
        {
            linkCommand.Transaction = transaction;
            linkCommand.CommandText =
                "SELECT task_sequence FROM report_tasks WHERE report_sequence = $reportSequence;";
            linkCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            using var reader = linkCommand.ExecuteReader();
            while (reader.Read())
            {
                linkedTasks.Add(reader.GetInt64(0));
            }
        }

        if (linkedTasks.Count > 1)
        {
            throw new InvalidOperationException("A resolved report with multiple linked tasks cannot be reprocessed.");
        }

        long? taskSequence = linkedTasks.Count == 0 ? null : linkedTasks[0];
        if (taskSequence is not null)
        {
            using var sourceCommand = connection.CreateCommand();
            sourceCommand.Transaction = transaction;
            sourceCommand.CommandText =
                "SELECT COUNT(*) FROM report_tasks WHERE task_sequence = $taskSequence;";
            sourceCommand.Parameters.AddWithValue("$taskSequence", taskSequence.Value);
            var sourceCount = Convert.ToInt64(sourceCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (sourceCount != 1)
            {
                throw new InvalidOperationException("A task shared by multiple reports cannot be deleted for reprocessing.");
            }
        }

        if (proposalState is not null && proposalState != TaskProposalStates.Confirmed)
        {
            throw new InvalidOperationException(
                $"A resolved report with an existing {proposalState} proposal cannot be reprocessed.");
        }

        if (taskSequence is not null)
        {
            using (var deleteLinkCommand = connection.CreateCommand())
            {
                deleteLinkCommand.Transaction = transaction;
                deleteLinkCommand.CommandText =
                    "DELETE FROM report_tasks WHERE report_sequence = $reportSequence AND task_sequence = $taskSequence;";
                deleteLinkCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
                deleteLinkCommand.Parameters.AddWithValue("$taskSequence", taskSequence.Value);
                EnsureSingleTransition(deleteLinkCommand, "The report-task link could not be deleted.");
            }

            using var deleteTaskCommand = connection.CreateCommand();
            deleteTaskCommand.Transaction = transaction;
            deleteTaskCommand.CommandText = "DELETE FROM tasks WHERE sequence = $taskSequence;";
            deleteTaskCommand.Parameters.AddWithValue("$taskSequence", taskSequence.Value);
            EnsureSingleTransition(deleteTaskCommand, "The linked task could not be deleted.");
        }

        using (var reportCommand = connection.CreateCommand())
        {
            reportCommand.Transaction = transaction;
            reportCommand.CommandText =
                "UPDATE reports SET status = 'open', updated_at = $updatedAt "
                + "WHERE sequence = $reportSequence AND status = 'resolved';";
            reportCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            reportCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            EnsureSingleTransition(reportCommand, "The resolved report could not be reopened.");
        }

        if (proposalId is null)
        {
            using var proposalCommand = connection.CreateCommand();
            proposalCommand.Transaction = transaction;
            proposalCommand.CommandText =
                """
                INSERT INTO task_proposals (
                  report_sequence, project_id, state, review_intent,
                  requested_rejection_reason, requested_clarification,
                  attempts, created_at, updated_at
                ) VALUES (
                  $reportSequence, $projectId, 'queued', 'proposal', NULL, NULL,
                  0, $createdAt, $updatedAt
                )
                RETURNING id;
                """;
            proposalCommand.Parameters.AddWithValue("$reportSequence", reportSequence);
            proposalCommand.Parameters.AddWithValue("$projectId", projectId);
            proposalCommand.Parameters.AddWithValue("$createdAt", timestamp);
            proposalCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            proposalId = Convert.ToInt64(proposalCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            using var proposalCommand = connection.CreateCommand();
            proposalCommand.Transaction = transaction;
            proposalCommand.CommandText =
                """
                UPDATE task_proposals
                SET state = 'queued', review_intent = 'proposal',
                    requested_rejection_reason = NULL, requested_clarification = NULL,
                    relevance = NULL, recommended_action = NULL, final_rejection_reason = NULL,
                    verification_summary = NULL, title = NULL, description = NULL,
                    acceptance_criteria = NULL, localized_report_title = NULL,
                    localized_report_observation = NULL, localized_report_impact = NULL,
                    localized_report_suggested_action = NULL, error = NULL, updated_at = $updatedAt
                WHERE id = $id AND state = 'confirmed';
                """;
            proposalCommand.Parameters.AddWithValue("$updatedAt", timestamp);
            proposalCommand.Parameters.AddWithValue("$id", proposalId.Value);
            EnsureSingleTransition(proposalCommand, "The confirmed proposal could not be queued for reprocessing.");
        }

        var proposal = GetProposal(connection, transaction, proposalId.Value)
            ?? throw new InvalidOperationException("The reprocessed proposal could not be read back.");
        var workflow = GetNoteWorkflow(connection, transaction, reportSequence)
            ?? throw new InvalidOperationException("The reprocessed workflow could not be read back.");
        transaction.Commit();
        return new ReprocessedNote(workflow, proposal, taskSequence is null ? null : FormatTaskId(taskSequence.Value));
    }

    public BoyScoutTask? UpdateStatus(string id, string status)
    {
        if (!TaskStatuses.IsValid(status))
        {
            throw new ArgumentException($"Unsupported task status: {status}.", nameof(status));
        }

        var sequence = ParseTaskId(id);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE tasks SET status = $status, updated_at = $updatedAt WHERE sequence = $sequence;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updatedAt", UtcTimestamp());
        command.Parameters.AddWithValue("$sequence", sequence);
        return command.ExecuteNonQuery() == 0
            ? null
            : GetTask(connection, transaction: null, sequence);
    }

    private static NoteWorkflowInfo? GetNoteWorkflow(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long reportSequence)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
              reports.sequence,
              reports.status,
              task_proposals.state,
              task_proposals.relevance,
              task_proposals.recommended_action,
              task_proposals.final_rejection_reason,
              CASE
                WHEN task_proposals.final_rejection_reason IS NOT NULL
                  THEN task_proposals.final_rejection_reason
                WHEN task_proposals.verification_summary IS NOT NULL
                  THEN task_proposals.verification_summary
                WHEN task_proposals.state = 'failed' THEN task_proposals.error
                ELSE NULL
              END,
              CASE
                WHEN task_proposals.review_intent = 'rejection'
                 AND task_proposals.state IN ('ready', 'unverified', 'rejected')
                  THEN task_proposals.requested_rejection_reason
                ELSE NULL
              END,
              CASE
                WHEN (task_proposals.review_intent = 'rejection'
                      OR task_proposals.relevance = 'obsolete')
                 AND task_proposals.state IN ('ready', 'unverified', 'rejected')
                  THEN task_proposals.verification_summary
                ELSE NULL
              END,
              COALESCE(task_proposals.updated_at, reports.updated_at)
            FROM reports
            LEFT JOIN task_proposals ON task_proposals.report_sequence = reports.sequence
            WHERE reports.sequence = $reportSequence;
            """;
        command.Parameters.AddWithValue("$reportSequence", reportSequence);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadNoteWorkflow(reader) : null;
    }

    private static NoteWorkflowInfo ReadNoteWorkflow(SqliteDataReader reader)
    {
        var reportStatus = reader.GetString(1);
        var proposalState = NullableString(reader, 2);
        var noteState = reportStatus switch
        {
            ReportStatuses.Dismissed => "rejected",
            ReportStatuses.Resolved => "processed",
            _ => "new",
        };
        var reviewState = reportStatus switch
        {
            ReportStatuses.Dismissed => "rejected",
            ReportStatuses.Resolved => "processed",
            _ => proposalState switch
            {
                TaskProposalStates.Processing => "checking",
                TaskProposalStates.Ready or TaskProposalStates.Unverified or TaskProposalStates.Failed =>
                    "awaiting_decision",
                TaskProposalStates.Confirmed => "processed",
                TaskProposalStates.Rejected => "rejected",
                _ => "queued",
            },
        };

        return new NoteWorkflowInfo(
            FormatReportId(reader.GetInt64(0)),
            noteState,
            reviewState,
            NullableString(reader, 3),
            NullableString(reader, 4),
            NullableString(reader, 5),
            NullableString(reader, 6),
            NullableString(reader, 7),
            NullableString(reader, 8),
            reader.GetString(9));
    }

    private static TaskProposal? GetProposal(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long id)
    {
        using var command = CreateProposalSelect(connection, transaction);
        command.CommandText += " WHERE task_proposals.id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return ReadProposals(command).SingleOrDefault();
    }

    private static TaskProposal? GetProposalByReportSequence(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long reportSequence)
    {
        using var command = CreateProposalSelect(connection, transaction);
        command.CommandText += " WHERE task_proposals.report_sequence = $reportSequence;";
        command.Parameters.AddWithValue("$reportSequence", reportSequence);
        return ReadProposals(command).SingleOrDefault();
    }

    private static SqliteCommand CreateProposalSelect(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
              task_proposals.id, task_proposals.report_sequence, task_proposals.project_id,
              task_proposals.state, task_proposals.review_intent,
              task_proposals.requested_rejection_reason, task_proposals.requested_clarification,
              task_proposals.relevance, task_proposals.recommended_action,
              task_proposals.final_rejection_reason, task_proposals.verification_summary,
              task_proposals.title, task_proposals.description,
              task_proposals.acceptance_criteria, task_proposals.error, task_proposals.attempts,
              task_proposals.created_at, task_proposals.updated_at,
              reports.project, reports.title, reports.location, reports.observation,
              reports.impact, reports.suggested_action, reports.discovered_during,
              task_proposals.localized_report_title,
              task_proposals.localized_report_observation,
              task_proposals.localized_report_impact,
              task_proposals.localized_report_suggested_action
            FROM task_proposals
            JOIN reports ON reports.sequence = task_proposals.report_sequence
            """;
        return command;
    }

    private static IReadOnlyList<TaskProposal> ReadProposals(SqliteCommand command)
    {
        var proposals = new List<TaskProposal>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            proposals.Add(
                new TaskProposal(
                    reader.GetInt64(0),
                    FormatReportId(reader.GetInt64(1)),
                    reader.GetInt64(2),
                    reader.GetString(18),
                    reader.GetString(3),
                    reader.GetString(4),
                    NullableString(reader, 5),
                    NullableString(reader, 6),
                    NullableString(reader, 7),
                    NullableString(reader, 8),
                    NullableString(reader, 9),
                    NullableString(reader, 10),
                    NullableString(reader, 11),
                    NullableString(reader, 12),
                    NullableString(reader, 13),
                    NullableString(reader, 14),
                    reader.GetInt32(15),
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(19),
                    NullableString(reader, 20),
                    reader.GetString(21),
                    NullableString(reader, 22),
                    NullableString(reader, 23),
                    NullableString(reader, 24),
                    NullableString(reader, 25),
                    NullableString(reader, 26),
                    NullableString(reader, 27),
                    NullableString(reader, 28)));
        }

        return proposals;
    }

    private static BoyScoutTask? GetTask(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long sequence)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT tasks.sequence, tasks.project_id, tasks.title, tasks.description,
                   tasks.acceptance_criteria, tasks.status, tasks.created_at, tasks.updated_at,
                   COALESCE((
                     SELECT group_concat(source.report_sequence, ',')
                     FROM (
                       SELECT report_sequence
                       FROM report_tasks
                       WHERE task_sequence = tasks.sequence
                       ORDER BY report_sequence
                     ) AS source
                   ), '')
            FROM tasks
            WHERE tasks.sequence = $sequence;
            """;
        command.Parameters.AddWithValue("$sequence", sequence);
        return ReadTasks(command).SingleOrDefault();
    }

    private static IReadOnlyList<BoyScoutTask> ReadTasks(SqliteCommand command)
    {
        var tasks = new List<BoyScoutTask>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tasks.Add(
                new BoyScoutTask(
                    FormatTaskId(reader.GetInt64(0)),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    ParseReportIds(reader.GetString(8))));
        }

        return tasks;
    }

    private static void EnsureSingleTransition(SqliteCommand command, string message)
    {
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static string FormatReportId(long sequence) => $"BSR-{sequence:D6}";

    private static string FormatTaskId(long sequence) => $"BST-{sequence:D6}";

    private static IReadOnlyList<string> ParseReportIds(string sequences) =>
        string.IsNullOrEmpty(sequences)
            ? []
            : sequences.Split(',').Select(long.Parse).Select(FormatReportId).ToArray();

    private static long ParseReportId(string id)
    {
        if (id is null ||
            id.Length != 10 ||
            !id.StartsWith("BSR-", StringComparison.Ordinal) ||
            !long.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ||
            sequence <= 0)
        {
            throw new ArgumentException($"Invalid Boy Scout report ID: {id}", nameof(id));
        }

        return sequence;
    }

    private static long ParseTaskId(string id)
    {
        if (id.Length != 10 ||
            !id.StartsWith("BST-", StringComparison.Ordinal) ||
            !long.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ||
            sequence <= 0)
        {
            throw new ArgumentException($"Invalid Boy Scout task ID: {id}", nameof(id));
        }

        return sequence;
    }

    private static string UtcTimestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static void ValidatePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be positive.");
        }
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}
