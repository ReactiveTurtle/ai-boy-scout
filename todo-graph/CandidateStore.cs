using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace TodoGraph;

public sealed class CandidateStore
{
    private static readonly Regex Words = new("[\\p{L}\\p{N}]{4,}", RegexOptions.Compiled);
    private static readonly Regex SearchWords = new("[\\p{L}\\p{N}]{2,}", RegexOptions.Compiled);
    private readonly string connectionString;

    public CandidateStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS candidates (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              source_report_id TEXT NOT NULL UNIQUE,
              project TEXT NOT NULL,
              title TEXT NOT NULL,
              observation TEXT NOT NULL,
              location TEXT,
              impact TEXT,
              suggested_action TEXT,
              discovered_during TEXT,
              source_status TEXT NOT NULL,
              source_created_at TEXT NOT NULL,
              received_at TEXT NOT NULL,
              source_url TEXT NOT NULL,
              triage_status TEXT NOT NULL DEFAULT 'pending',
              localized_title TEXT,
              localized_description TEXT,
              source_project_id INTEGER,
              created_by TEXT,
              source TEXT NOT NULL DEFAULT 'boyscout',
              raw_text TEXT
            );
            CREATE TABLE IF NOT EXISTS candidate_clarification_rounds (
              candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              round INTEGER NOT NULL,
              questions_json TEXT NOT NULL,
              answer TEXT,
              created_at TEXT NOT NULL,
              PRIMARY KEY(candidate_id, round)
            );
            CREATE TABLE IF NOT EXISTS candidate_links (
              candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              related_candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              relation TEXT NOT NULL,
              created_at TEXT NOT NULL,
              PRIMARY KEY(candidate_id, related_candidate_id, relation),
              CHECK(candidate_id <> related_candidate_id)
            );
            CREATE INDEX IF NOT EXISTS candidates_queue ON candidates(triage_status, id DESC);
            CREATE TABLE IF NOT EXISTS candidate_agent_reviews (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              summary TEXT NOT NULL,
              context_json TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS candidate_agent_suggestions (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              review_id INTEGER NOT NULL REFERENCES candidate_agent_reviews(id) ON DELETE CASCADE,
              candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              relation TEXT NOT NULL,
              rationale TEXT NOT NULL,
              status TEXT NOT NULL DEFAULT 'pending',
              UNIQUE(review_id, candidate_id, relation)
            );
            CREATE TABLE IF NOT EXISTS candidate_agent_jobs (
              candidate_id INTEGER PRIMARY KEY REFERENCES candidates(id) ON DELETE CASCADE,
              status TEXT NOT NULL,
              attempts INTEGER NOT NULL DEFAULT 0,
              error TEXT,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS task_nodes (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              source_project_id INTEGER,
              source_task_id TEXT,
              source_candidate_id INTEGER UNIQUE REFERENCES candidates(id),
              project TEXT NOT NULL,
              title TEXT NOT NULL,
              description TEXT NOT NULL,
              acceptance_criteria TEXT NOT NULL,
              current_consequences TEXT,
              status TEXT NOT NULL DEFAULT 'ready',
              source_report_ids_json TEXT NOT NULL DEFAULT '[]',
              merge_request_url TEXT,
              merge_request_status TEXT NOT NULL DEFAULT 'none',
              merge_request_source_branch TEXT,
              approval_state TEXT NOT NULL DEFAULT 'unknown',
              mergeability TEXT NOT NULL DEFAULT 'unknown',
              approved_count INTEGER,
              required_approvals INTEGER,
              merge_request_target_branch TEXT,
              created_by TEXT,
              execution_status TEXT NOT NULL DEFAULT 'idle',
              build_status TEXT NOT NULL DEFAULT 'not_started',
              execution_error TEXT,
              worktree_path TEXT,
              branch_name TEXT,
              base_branch TEXT,
              commit_sha TEXT,
              user_modified INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              UNIQUE(source_project_id, source_task_id)
            );
            CREATE TABLE IF NOT EXISTS project_repositories (
              source_project_id INTEGER PRIMARY KEY,
              project TEXT NOT NULL,
              repository_path TEXT NOT NULL,
              code_host TEXT NOT NULL DEFAULT 'gitlab',
              remote_repository_url TEXT,
              updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS task_preflights (
              task_id INTEGER PRIMARY KEY REFERENCES task_nodes(id) ON DELETE CASCADE,
              result_json TEXT NOT NULL,
              answers TEXT,
              clarification_pending INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS candidate_task_links (
              candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              task_id INTEGER NOT NULL REFERENCES task_nodes(id) ON DELETE CASCADE,
              relation TEXT NOT NULL,
              created_at TEXT NOT NULL,
              PRIMARY KEY(candidate_id, task_id, relation)
            );
            CREATE TABLE IF NOT EXISTS task_links (
              task_id INTEGER NOT NULL REFERENCES task_nodes(id) ON DELETE CASCADE,
              related_task_id INTEGER NOT NULL REFERENCES task_nodes(id) ON DELETE CASCADE,
              relation TEXT NOT NULL,
              created_at TEXT NOT NULL,
              PRIMARY KEY(task_id, related_task_id, relation),
              CHECK(task_id <> related_task_id)
            );
            CREATE TABLE IF NOT EXISTS merge_request_comments (
              task_id INTEGER NOT NULL REFERENCES task_nodes(id) ON DELETE CASCADE,
              merge_request_url TEXT NOT NULL,
              external_comment_id TEXT NOT NULL,
              discussion_id TEXT NOT NULL,
              body TEXT NOT NULL,
              author TEXT NOT NULL,
              comment_created_at TEXT NOT NULL,
              comment_url TEXT,
              observed_at TEXT NOT NULL,
              reviewed_at TEXT,
              reply_body TEXT,
              reply_status TEXT,
              reply_error TEXT,
              reply_external_id TEXT,
              reply_sent_at TEXT,
              agent_handled_at TEXT,
              PRIMARY KEY(task_id, merge_request_url, external_comment_id)
            );
            CREATE TABLE IF NOT EXISTS candidate_agent_task_plans (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              review_id INTEGER NOT NULL UNIQUE REFERENCES candidate_agent_reviews(id) ON DELETE CASCADE,
              candidate_id INTEGER NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
              action TEXT NOT NULL,
              target_task_id INTEGER REFERENCES task_nodes(id),
              title TEXT NOT NULL,
              description TEXT NOT NULL,
              acceptance_criteria TEXT NOT NULL,
              rationale TEXT NOT NULL,
              status TEXT NOT NULL DEFAULT 'pending',
              created_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureCandidateColumn(connection, "localized_title");
        EnsureCandidateColumn(connection, "localized_description");
        EnsureCandidateColumn(connection, "source_project_id");
        EnsureCandidateColumn(connection, "created_by");
        EnsureCandidateColumn(connection, "source", "TEXT NOT NULL DEFAULT 'boyscout'");
        EnsureCandidateColumn(connection, "raw_text");
        EnsureTaskColumn(connection, "merge_request_url");
        EnsureTaskColumn(connection, "execution_status");
        EnsureTaskColumn(connection, "build_status");
        EnsureTaskColumn(connection, "execution_error");
        EnsureTaskColumn(connection, "worktree_path");
        EnsureTaskColumn(connection, "branch_name");
        EnsureTaskColumn(connection, "base_branch");
        EnsureTaskColumn(connection, "commit_sha");
        EnsureTaskColumn(connection, "created_by");
        if (EnsureTaskColumn(connection, "merge_request_status"))
        {
            using var legacyMr = connection.CreateCommand();
            legacyMr.CommandText = "UPDATE task_nodes SET merge_request_status = 'open' WHERE status = 'code_review' AND merge_request_url IS NOT NULL;";
            legacyMr.ExecuteNonQuery();
        }
        EnsureTaskColumn(connection, "merge_request_source_branch");
        EnsureTaskColumn(connection, "approval_state");
        EnsureTaskColumn(connection, "mergeability");
        EnsureTaskColumn(connection, "approved_count");
        EnsureTaskColumn(connection, "required_approvals");
        EnsureTaskColumn(connection, "merge_request_target_branch");
        EnsureTaskPreflightColumn(connection, "clarification_pending");
        EnsureProjectRepositoryColumn(connection, "code_host");
        EnsureProjectRepositoryColumn(connection, "remote_repository_url");
        using (var interrupted = connection.CreateCommand())
        {
            interrupted.CommandText = "UPDATE task_nodes SET execution_status = 'queued' WHERE execution_status = 'clarifying' AND status <> 'closed';";
            interrupted.ExecuteNonQuery();
        }
        using (var interruptedPreparation = connection.CreateCommand())
        {
            interruptedPreparation.CommandText = "UPDATE task_nodes SET execution_status = 'blocked', execution_error = 'Worktree preparation was interrupted. Check the saved branch and worktree before resuming.' WHERE execution_status = 'preparing';";
            interruptedPreparation.ExecuteNonQuery();
        }
        using (var interruptedDevelopment = connection.CreateCommand())
        {
            interruptedDevelopment.CommandText = "UPDATE task_nodes SET execution_status = 'blocked', execution_error = 'Development was interrupted. Inspect the worktree before resuming.' WHERE execution_status = 'running';";
            interruptedDevelopment.ExecuteNonQuery();
        }
        using (var interruptedCommit = connection.CreateCommand())
        {
            interruptedCommit.CommandText = "UPDATE task_nodes SET execution_status = 'blocked', execution_error = 'Commit was interrupted. Inspect the worktree and commit SHA before retrying.' WHERE execution_status = 'committing';";
            interruptedCommit.ExecuteNonQuery();
        }
        using (var interruptedMr = connection.CreateCommand())
        {
            interruptedMr.CommandText = "UPDATE task_nodes SET execution_status = 'mr_retry_required', execution_error = 'MR creation was interrupted. Check GitLab for an existing MR from the saved branch before retrying.' WHERE execution_status = 'creating_mr';";
            interruptedMr.ExecuteNonQuery();
        }
        EnsureMergeRequestCommentColumn(connection, "reviewed_at");
        EnsureMergeRequestCommentColumn(connection, "reply_body");
        EnsureMergeRequestCommentColumn(connection, "reply_status");
        EnsureMergeRequestCommentColumn(connection, "reply_error");
        EnsureMergeRequestCommentColumn(connection, "reply_external_id");
        EnsureMergeRequestCommentColumn(connection, "reply_sent_at");
        EnsureMergeRequestCommentColumn(connection, "agent_handled_at");
        using (var interruptedReplies = connection.CreateCommand())
        {
            interruptedReplies.CommandText = "UPDATE merge_request_comments SET reply_status = 'failed', reply_error = 'Delivery outcome is unknown because the application stopped while sending. Verify GitLab before resubmitting.' WHERE reply_status = 'sending';";
            interruptedReplies.ExecuteNonQuery();
        }
        using var jobMigration = connection.CreateCommand();
        jobMigration.CommandText =
            """
            INSERT INTO candidate_agent_jobs(candidate_id, status, created_at, updated_at)
            SELECT c.id, 'queued', datetime('now'), datetime('now')
            FROM candidates c
            WHERE c.localized_title IS NULL
              AND NOT EXISTS (SELECT 1 FROM candidate_agent_jobs j WHERE j.candidate_id = c.id);
            UPDATE candidate_agent_jobs
            SET status = 'queued', error = NULL, updated_at = datetime('now')
            WHERE status = 'ready' AND EXISTS (
              SELECT 1 FROM candidates c
              WHERE c.id = candidate_agent_jobs.candidate_id AND c.localized_title IS NULL
            );
            UPDATE candidate_agent_jobs
            SET status = 'queued', error = 'Worker stopped before finishing the previous run.', updated_at = datetime('now')
            WHERE status = 'running';
            """;
        jobMigration.ExecuteNonQuery();
        using var history = connection.CreateCommand();
        history.CommandText =
            """
            CREATE TABLE IF NOT EXISTS task_history (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              task_id INTEGER NOT NULL,
              event_type TEXT NOT NULL,
              previous_value TEXT,
              new_value TEXT,
              occurred_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS task_history_task ON task_history(task_id, id DESC);
            CREATE TRIGGER IF NOT EXISTS task_history_created AFTER INSERT ON task_nodes BEGIN
              INSERT INTO task_history(task_id, event_type, new_value, occurred_at) VALUES(NEW.id, 'created', NEW.title, COALESCE(NEW.created_at, datetime('now')));
            END;
            CREATE TRIGGER IF NOT EXISTS task_history_status AFTER UPDATE OF status ON task_nodes WHEN OLD.status IS NOT NEW.status BEGIN
              INSERT INTO task_history(task_id, event_type, previous_value, new_value, occurred_at) VALUES(NEW.id, 'status', OLD.status, NEW.status, COALESCE(NEW.updated_at, datetime('now')));
            END;
            CREATE TRIGGER IF NOT EXISTS task_history_execution AFTER UPDATE OF execution_status ON task_nodes WHEN OLD.execution_status IS NOT NEW.execution_status BEGIN
              INSERT INTO task_history(task_id, event_type, previous_value, new_value, occurred_at) VALUES(NEW.id, 'execution', OLD.execution_status, NEW.execution_status, COALESCE(NEW.updated_at, datetime('now')));
            END;
            CREATE TRIGGER IF NOT EXISTS task_history_build AFTER UPDATE OF build_status ON task_nodes WHEN OLD.build_status IS NOT NEW.build_status BEGIN
              INSERT INTO task_history(task_id, event_type, previous_value, new_value, occurred_at) VALUES(NEW.id, 'build', OLD.build_status, NEW.build_status, COALESCE(NEW.updated_at, datetime('now')));
            END;
            CREATE TRIGGER IF NOT EXISTS task_history_content AFTER UPDATE OF title, description, acceptance_criteria, current_consequences ON task_nodes
              WHEN OLD.title IS NOT NEW.title OR OLD.description IS NOT NEW.description OR OLD.acceptance_criteria IS NOT NEW.acceptance_criteria OR OLD.current_consequences IS NOT NEW.current_consequences BEGIN
              INSERT INTO task_history(task_id, event_type, previous_value, new_value, occurred_at)
              VALUES(NEW.id, 'content', json_object('title', OLD.title, 'description', OLD.description, 'acceptanceCriteria', OLD.acceptance_criteria, 'currentConsequences', OLD.current_consequences),
                json_object('title', NEW.title, 'description', NEW.description, 'acceptanceCriteria', NEW.acceptance_criteria, 'currentConsequences', NEW.current_consequences), COALESCE(NEW.updated_at, datetime('now')));
            END;
            """;
        history.ExecuteNonQuery();
    }

    public (Candidate Candidate, bool AlreadyReceived) Receive(CandidateImport input)
    {
        Validate(input);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO candidates (
              source_report_id, project, title, observation, location, impact,
              suggested_action, discovered_during, source_status, source_created_at, received_at, source_url, source_project_id, created_by
            ) VALUES (
              $reportId, $project, $title, $observation, $location, $impact,
              $suggestedAction, $discoveredDuring, $status, $createdAt, $receivedAt, $sourceUrl, $sourceProjectId, $createdBy
            )
            ON CONFLICT(source_report_id) DO NOTHING;
            """;
        AddInput(command, input);
        command.Parameters.AddWithValue("$receivedAt", UtcNow());
        command.Parameters.AddWithValue("$sourceUrl", input.SourceUrl);
        command.Parameters.AddWithValue("$sourceProjectId", input.SourceProjectId is null ? DBNull.Value : input.SourceProjectId.Value);
        command.Parameters.AddWithValue("$createdBy", Db(input.CreatedBy));
        var changed = command.ExecuteNonQuery();
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM candidates WHERE source_report_id = $reportId;";
        select.Parameters.AddWithValue("$reportId", input.SourceReportId);
        var id = Convert.ToInt64(select.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (input.SourceProjectId is not null)
        {
            using var updateProject = connection.CreateCommand();
            updateProject.Transaction = transaction;
            updateProject.CommandText = "UPDATE candidates SET source_project_id = COALESCE(source_project_id, $projectId) WHERE id = $id;";
            updateProject.Parameters.AddWithValue("$projectId", input.SourceProjectId.Value);
            updateProject.Parameters.AddWithValue("$id", id);
            updateProject.ExecuteNonQuery();
        }
        if (input.SourceTasks is { Count: > 0 }) ImportTasks(connection, transaction, input.SourceTasks, id, input.SourceReportId);
        if (changed > 0)
        {
            using var enqueue = connection.CreateCommand();
            enqueue.Transaction = transaction;
            enqueue.CommandText = "INSERT INTO candidate_agent_jobs(candidate_id, status, created_at, updated_at) VALUES($id, 'queued', $now, $now) ON CONFLICT(candidate_id) DO NOTHING;";
            enqueue.Parameters.AddWithValue("$id", id);
            enqueue.Parameters.AddWithValue("$now", UtcNow());
            enqueue.ExecuteNonQuery();
        }
        transaction.Commit();
        return (Get(id)!, changed == 0);
    }

    public long? TryClaimAgentJob()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE candidate_agent_jobs
            SET status = 'running', attempts = attempts + 1, error = NULL, updated_at = $now
            WHERE candidate_id = (
              SELECT candidate_id FROM candidate_agent_jobs WHERE status = 'queued' ORDER BY created_at, candidate_id LIMIT 1
            )
            RETURNING candidate_id;
            """;
        command.Parameters.AddWithValue("$now", UtcNow());
        var result = command.ExecuteScalar();
        transaction.Commit();
        return result is null ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public CandidateAgentJob? GetAgentJob(long candidateId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT candidate_id, status, attempts, error, updated_at FROM candidate_agent_jobs WHERE candidate_id = $id;";
        command.Parameters.AddWithValue("$id", candidateId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new CandidateAgentJob(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), NullableString(reader, 3), reader.GetString(4))
            : null;
    }

    public CandidateAgentJob? SetAgentJobState(long candidateId, string status, string? error = null)
    {
        if (status is not ("queued" or "ready" or "failed"))
            throw new ArgumentException("Unsupported agent job state.", nameof(status));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE candidate_agent_jobs SET status = $status, error = $error, updated_at = $updatedAt WHERE candidate_id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$error", Db(error is null ? null : error.Length > 2000 ? error[..2000] : error));
        command.Parameters.AddWithValue("$updatedAt", UtcNow());
        command.Parameters.AddWithValue("$id", candidateId);
        return command.ExecuteNonQuery() == 0 ? null : GetAgentJob(candidateId);
    }

    public CandidateAgentJob? QueueAgentJob(long candidateId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE candidate_agent_jobs SET status = 'queued', error = NULL, updated_at = $updatedAt WHERE candidate_id = $id AND status IN ('ready', 'failed');";
        command.Parameters.AddWithValue("$updatedAt", UtcNow());
        command.Parameters.AddWithValue("$id", candidateId);
        command.ExecuteNonQuery();
        return GetAgentJob(candidateId);
    }

    public IReadOnlyList<TaskNode> ListTaskNodes(long? sourceProjectId = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sourceProjectId is null
            ? "SELECT t.id, t.source_project_id, t.source_task_id, t.project, t.title, t.description, t.acceptance_criteria, t.current_consequences, t.status, t.source_report_ids_json, t.user_modified, t.created_at, t.updated_at, t.merge_request_url, t.execution_status, t.build_status, r.repository_path, t.execution_error, t.worktree_path, t.branch_name, t.base_branch, t.merge_request_status, t.commit_sha, t.merge_request_source_branch, t.created_by, COALESCE(r.code_host, 'gitlab'), t.approval_state, t.mergeability, t.approved_count, t.required_approvals, t.merge_request_target_branch, r.remote_repository_url FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id ORDER BY t.updated_at DESC, t.id DESC;"
            : "SELECT t.id, t.source_project_id, t.source_task_id, t.project, t.title, t.description, t.acceptance_criteria, t.current_consequences, t.status, t.source_report_ids_json, t.user_modified, t.created_at, t.updated_at, t.merge_request_url, t.execution_status, t.build_status, r.repository_path, t.execution_error, t.worktree_path, t.branch_name, t.base_branch, t.merge_request_status, t.commit_sha, t.merge_request_source_branch, t.created_by, COALESCE(r.code_host, 'gitlab'), t.approval_state, t.mergeability, t.approved_count, t.required_approvals, t.merge_request_target_branch, r.remote_repository_url FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id WHERE t.source_project_id = $projectId ORDER BY t.updated_at DESC, t.id DESC;";
        if (sourceProjectId is not null) command.Parameters.AddWithValue("$projectId", sourceProjectId.Value);
        using var reader = command.ExecuteReader();
        var tasks = new List<TaskNode>();
        while (reader.Read()) tasks.Add(MapTaskNode(reader));
        return tasks;
    }

    public bool AddTaskLink(long id, TaskLinkRequest request)
    {
        if (request.Relation is not ("related" or "duplicate" or "depends_on" or "blocks"))
            throw new ArgumentException("Unsupported task relationship.", nameof(request));
        if (id == request.RelatedTaskId)
            throw new ArgumentException("A task cannot link to itself.", nameof(request));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO task_links(task_id, related_task_id, relation, created_at)
            SELECT $id, $relatedId, $relation, $createdAt
            WHERE EXISTS (SELECT 1 FROM task_nodes WHERE id = $id)
              AND EXISTS (SELECT 1 FROM task_nodes WHERE id = $relatedId)
            ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$relatedId", request.RelatedTaskId);
        command.Parameters.AddWithValue("$relation", request.Relation);
        command.Parameters.AddWithValue("$createdAt", UtcNow());
        return command.ExecuteNonQuery() > 0 || TaskLinkExists(connection, id, request);
    }

    private static bool TaskLinkExists(SqliteConnection connection, long id, TaskLinkRequest request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM task_links WHERE task_id = $id AND related_task_id = $relatedId AND relation = $relation);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$relatedId", request.RelatedTaskId);
        command.Parameters.AddWithValue("$relation", request.Relation);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    public GraphSnapshot GetGraphSnapshot()
    {
        using var connection = Open();
        var nodes = new List<GraphNodeSnapshot>();
        using (var candidates = connection.CreateCommand())
        {
            candidates.CommandText =
                "SELECT id, source_report_id, COALESCE(localized_title, title), project, COALESCE(localized_description, observation), triage_status, created_by, source FROM candidates WHERE triage_status <> 'dismissed' ORDER BY id;";
            using var reader = candidates.ExecuteReader();
            while (reader.Read())
                nodes.Add(new($"candidate:{reader.GetInt64(0)}", "candidate", reader.GetInt64(0), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(1), CreatedBy: NullableString(reader, 6), Source: reader.GetString(7)));
        }
        using (var tasks = connection.CreateCommand())
        {
            tasks.CommandText = "SELECT t.id, t.source_task_id, t.project, t.title, t.description, t.status, t.acceptance_criteria, t.current_consequences, t.merge_request_url, t.execution_status, t.build_status, t.source_project_id, r.repository_path, t.execution_error, t.worktree_path, t.branch_name, t.base_branch, t.merge_request_status, t.commit_sha, t.merge_request_source_branch, t.created_by, COALESCE(r.code_host, 'gitlab'), t.approval_state, t.mergeability, t.approved_count, t.required_approvals, t.merge_request_target_branch, r.remote_repository_url, (SELECT COUNT(*) FROM merge_request_comments c WHERE c.task_id = t.id) FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id ORDER BY t.id;";
            using var reader = tasks.ExecuteReader();
            while (reader.Read())
                nodes.Add(new(
                    $"task:{reader.GetInt64(0)}", "task", reader.GetInt64(0), reader.GetString(3), reader.GetString(2), reader.GetString(4), reader.GetString(5), null,
                    NullableString(reader, 1), reader.GetString(6), NullableString(reader, 7), NullableString(reader, 8),
                    WorkflowEnumExtensions.ParseStorageString(reader.GetString(9), TaskExecutionStatus.Idle),
                    WorkflowEnumExtensions.ParseStorageString(reader.GetString(10), ProjectBuildStatus.NotStarted),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11), NullableString(reader, 12), NullableString(reader, 13),
                    NullableString(reader, 14), NullableString(reader, 15), NullableString(reader, 16),
                    WorkflowEnumExtensions.ParseStorageString(reader.GetString(17), MergeRequestStatus.None),
                    Directory.Exists(NullableString(reader, 14)), NullableString(reader, 18), NullableString(reader, 19), NullableString(reader, 20),
                    WorkflowEnumExtensions.ParseStorageString(reader.GetString(21), CodeHost.Gitlab),
                    WorkflowEnumExtensions.ParseStorageString(reader.GetString(22), MergeApprovalState.Unknown),
                    WorkflowEnumExtensions.ParseStorageString(reader.GetString(23), MergeabilityState.Unknown),
                    reader.IsDBNull(24) ? null : reader.GetInt32(24), reader.IsDBNull(25) ? null : reader.GetInt32(25), NullableString(reader, 26), NullableString(reader, 27), reader.GetInt32(28)));
        }

        var edges = new List<GraphEdgeSnapshot>();
        using (var links = connection.CreateCommand())
        {
            links.CommandText = "SELECT candidate_id, related_candidate_id, relation FROM candidate_links ORDER BY candidate_id, related_candidate_id;";
            using var reader = links.ExecuteReader();
            while (reader.Read()) edges.Add(new($"candidate:{reader.GetInt64(0)}", $"candidate:{reader.GetInt64(1)}", reader.GetString(2)));
        }
        using (var candidateTasks = connection.CreateCommand())
        {
            candidateTasks.CommandText = "SELECT candidate_id, task_id, relation FROM candidate_task_links ORDER BY candidate_id, task_id;";
            using var reader = candidateTasks.ExecuteReader();
            while (reader.Read()) edges.Add(new($"candidate:{reader.GetInt64(0)}", $"task:{reader.GetInt64(1)}", reader.GetString(2)));
        }
        using (var taskLinks = connection.CreateCommand())
        {
            taskLinks.CommandText = "SELECT task_id, related_task_id, relation FROM task_links ORDER BY task_id, related_task_id;";
            using var reader = taskLinks.ExecuteReader();
            while (reader.Read()) edges.Add(new($"task:{reader.GetInt64(0)}", $"task:{reader.GetInt64(1)}", reader.GetString(2)));
        }
        return new GraphSnapshot(nodes, edges);
    }

    public TaskNode? GetTaskNode(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT t.id, t.source_project_id, t.source_task_id, t.project, t.title, t.description, t.acceptance_criteria, t.current_consequences, t.status, t.source_report_ids_json, t.user_modified, t.created_at, t.updated_at, t.merge_request_url, t.execution_status, t.build_status, r.repository_path, t.execution_error, t.worktree_path, t.branch_name, t.base_branch, t.merge_request_status, t.commit_sha, t.merge_request_source_branch, t.created_by, COALESCE(r.code_host, 'gitlab'), t.approval_state, t.mergeability, t.approved_count, t.required_approvals, t.merge_request_target_branch, r.remote_repository_url FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id WHERE t.id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapTaskNode(reader) : null;
    }

    public IReadOnlyList<TaskHistoryEvent> ListTaskHistory(long taskId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, task_id, event_type, previous_value, new_value, occurred_at FROM task_history WHERE task_id = $id ORDER BY id;";
        command.Parameters.AddWithValue("$id", taskId);
        using var reader = command.ExecuteReader();
        var events = new List<TaskHistoryEvent>();
        while (reader.Read()) events.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), NullableString(reader, 3), NullableString(reader, 4), reader.GetString(5)));
        return events;
    }

    public IReadOnlyList<TaskFileMetadata> ListTaskFiles(long taskId) => ListFiles(GetTaskFileRoot(taskId));

    public IReadOnlyList<TaskFileMetadata> ListCandidateFiles(long candidateId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.repository_path FROM candidates c LEFT JOIN project_repositories r ON r.source_project_id = c.source_project_id WHERE c.id = $id;";
        command.Parameters.AddWithValue("$id", candidateId);
        var path = command.ExecuteScalar() as string;
        return ListFiles(path is not null && IsGitRepository(path) ? path : null);
    }

    public bool UpdateTaskDescription(long id, string description)
    {
        var value = ValidateDescription(description);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET description = $description, user_modified = 1, updated_at = $now WHERE id = $id;";
        command.Parameters.AddWithValue("$description", value);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public bool UpdateCandidateDescription(long id, string description)
    {
        var value = ValidateDescription(description);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE candidates SET localized_description = $description WHERE id = $id;";
        command.Parameters.AddWithValue("$description", value);
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    private string? GetTaskFileRoot(long taskId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT t.worktree_path, r.repository_path FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id WHERE t.id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var worktree = NullableString(reader, 0);
        var repository = NullableString(reader, 1);
        if (!string.IsNullOrWhiteSpace(worktree) && Directory.Exists(worktree)) return worktree;
        return !string.IsNullOrWhiteSpace(repository) && IsGitRepository(repository) ? repository : null;
    }

    private static IReadOnlyList<TaskFileMetadata> ListFiles(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var result = new List<TaskFileMetadata>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0 && result.Count < 1000)
        {
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(directory).ToArray(); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) || IsSensitiveFile(name)) continue;
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else
                    {
                        var info = new FileInfo(entry);
                        result.Add(new(Path.GetRelativePath(root, entry).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc));
                        if (result.Count == 1000) break;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return result.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static bool IsSensitiveFile(string name) =>
        name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        new[] { ".pem", ".key", ".p12", ".pfx", ".cer", ".crt", ".asc", ".gpg" }.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
        name is ".npmrc" or ".pypirc" or ".netrc" or "credentials" or "secrets.json" ||
        name.Equals("id_rsa", StringComparison.OrdinalIgnoreCase) || name.Equals("id_ed25519", StringComparison.OrdinalIgnoreCase);

    private static string ValidateDescription(string description)
    {
        var value = description?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 10000)
            throw new ArgumentException("Description must contain 1-10000 characters.", nameof(description));
        return value;
    }

    public IReadOnlyList<ProjectRepository> ListProjectRepositories()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "WITH project_names AS (SELECT source_project_id, project FROM task_nodes WHERE source_project_id IS NOT NULL UNION ALL SELECT source_project_id, project FROM candidates WHERE source_project_id IS NOT NULL) SELECT r.source_project_id, r.project, r.repository_path, r.updated_at, r.code_host, r.remote_repository_url FROM project_repositories r UNION ALL SELECT p.source_project_id, MIN(p.project), NULL, NULL, 'gitlab', NULL FROM project_names p WHERE NOT EXISTS (SELECT 1 FROM project_repositories r WHERE r.source_project_id = p.source_project_id) GROUP BY p.source_project_id ORDER BY 2;";
        using var reader = command.ExecuteReader();
        var projects = new List<ProjectRepository>();
        while (reader.Read())
            projects.Add(new(reader.GetInt64(0), reader.GetString(1), NullableString(reader, 2), NullableString(reader, 3),
                WorkflowEnumExtensions.ParseStorageString(reader.GetString(4), CodeHost.Gitlab), NullableString(reader, 5)));
        return projects;
    }

    public ProjectRepository CreateProject(string repositoryPath, CodeHost codeHost = CodeHost.Gitlab)
    {
        if (!Enum.IsDefined(codeHost)) throw new ArgumentException("Select a supported code host.", nameof(codeHost));
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Path.IsPathFullyQualified(repositoryPath.Trim()))
            throw new ArgumentException("Enter an absolute local project path.", nameof(repositoryPath));
        var fullPath = Path.GetFullPath(repositoryPath.Trim());
        if (!IsGitRepository(fullPath))
            throw new ArgumentException("The selected folder does not contain a Git repository (.git directory or file).", nameof(repositoryPath));

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        ProjectRepository? existingProject = null;
        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT source_project_id, project, repository_path, updated_at, code_host, remote_repository_url FROM project_repositories;";
            using var reader = existing.ExecuteReader();
            while (reader.Read())
            {
                var savedPath = reader.GetString(2);
                if (string.Equals(Path.GetFullPath(savedPath), fullPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    existingProject = new ProjectRepository(reader.GetInt64(0), reader.GetString(1), savedPath, NullableString(reader, 3),
                        WorkflowEnumExtensions.ParseStorageString(reader.GetString(4), CodeHost.Gitlab), NullableString(reader, 5));
                    break;
                }
            }
        }
        if (existingProject is not null)
        {
            transaction.Commit();
            return existingProject;
        }

        var name = new DirectoryInfo(fullPath).Name;
        if (string.IsNullOrWhiteSpace(name)) name = fullPath;
        var now = UtcNow();
        long projectId;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "SELECT MIN(source_project_id) FROM project_repositories WHERE source_project_id < 0;";
            var min = insert.ExecuteScalar();
            projectId = min is null or DBNull ? -1 : Convert.ToInt64(min, CultureInfo.InvariantCulture) - 1;
        }
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO project_repositories(source_project_id, project, repository_path, code_host, updated_at) VALUES($id, $project, $path, $codeHost, $updatedAt);";
            insert.Parameters.AddWithValue("$id", projectId);
            insert.Parameters.AddWithValue("$project", name);
            insert.Parameters.AddWithValue("$path", fullPath);
            insert.Parameters.AddWithValue("$codeHost", codeHost.ToStorageString());
            insert.Parameters.AddWithValue("$updatedAt", now);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
        return new ProjectRepository(projectId, name, fullPath, now, codeHost);
    }

    public TaskNode? CreateManualTask(CreateManualTaskRequest request, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(request.RepositoryPath) && request.ProjectId <= 0) throw new ArgumentOutOfRangeException(nameof(request.ProjectId));
        if (!string.IsNullOrWhiteSpace(request.RepositoryPath) && !Path.IsPathFullyQualified(request.RepositoryPath.Trim()))
            throw new ArgumentException("Project path must be absolute.", nameof(request));
        var title = request.Title?.Trim();
        var description = request.Description?.Trim();
        var criteria = string.IsNullOrWhiteSpace(request.AcceptanceCriteria) ? "Критерии уточняются." : request.AcceptanceCriteria.Trim();
        var consequences = string.IsNullOrWhiteSpace(request.CurrentConsequences) ? null : request.CurrentConsequences.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 300)
            throw new ArgumentException("Название задачи должно содержать от 1 до 300 символов.", nameof(request));
        if (string.IsNullOrWhiteSpace(description) || description.Length > 10000)
            throw new ArgumentException("Описание задачи должно содержать от 1 до 10000 символов.", nameof(request));
        if (criteria.Length > 4000 || consequences?.Length > 2000)
            throw new ArgumentException("Критерии ограничены 4000, последствия — 2000 символами.", nameof(request));
        var author = string.IsNullOrWhiteSpace(createdBy) ? Environment.UserName : createdBy.Trim();
        if (author.Length > 100) throw new ArgumentException("Имя автора ограничено 100 символами.", nameof(createdBy));

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        string? projectName = null;
        long projectId = request.ProjectId;
        if (!string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            var path = Path.GetFullPath(request.RepositoryPath.Trim());
            using var project = connection.CreateCommand();
            project.Transaction = transaction;
            project.CommandText = "SELECT source_project_id, project, repository_path FROM project_repositories;";
            using var reader = project.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(Path.GetFullPath(reader.GetString(2)), path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    projectId = reader.GetInt64(0);
                    projectName = reader.GetString(1);
                    break;
                }
            }
        }
        else
        {
            using var project = connection.CreateCommand();
            project.Transaction = transaction;
            project.CommandText = "SELECT COALESCE((SELECT MIN(project) FROM task_nodes WHERE source_project_id = $projectId), (SELECT MIN(project) FROM candidates WHERE source_project_id = $projectId));";
            project.Parameters.AddWithValue("$projectId", request.ProjectId);
            projectName = project.ExecuteScalar() as string;
        }
        if (projectName is null)
        {
            transaction.Rollback();
            return null;
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO task_nodes(source_project_id, project, title, description, acceptance_criteria, current_consequences, status, source_report_ids_json, created_by, user_modified, created_at, updated_at) VALUES($projectId, $project, $title, $description, $criteria, $consequences, 'todo', '[]', $createdBy, 1, $now, $now) RETURNING id;";
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$project", projectName);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$criteria", criteria);
        command.Parameters.AddWithValue("$consequences", Db(consequences));
        command.Parameters.AddWithValue("$createdBy", author);
        command.Parameters.AddWithValue("$now", UtcNow());
        var result = command.ExecuteScalar();
        if (result is null)
        {
            transaction.Rollback();
            return null;
        }
        var id = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        transaction.Commit();
        return GetTaskNode(id);
    }

    public ProjectRepository? SetProjectRepository(long sourceProjectId, string repositoryPath, CodeHost codeHost = CodeHost.Gitlab)
    {
        if (sourceProjectId == 0) throw new ArgumentOutOfRangeException(nameof(sourceProjectId));
        if (string.IsNullOrWhiteSpace(repositoryPath) || !Path.IsPathFullyQualified(repositoryPath.Trim()))
            throw new ArgumentException("Enter an absolute local repository path.", nameof(repositoryPath));
        var fullPath = Path.GetFullPath(repositoryPath.Trim());
        if (!IsGitRepository(fullPath))
            throw new ArgumentException("The selected folder does not contain a Git repository (.git directory or file).", nameof(repositoryPath));

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using (var pathOwners = connection.CreateCommand())
        {
            pathOwners.Transaction = transaction;
            pathOwners.CommandText = "SELECT source_project_id, repository_path FROM project_repositories;";
            using var reader = pathOwners.ExecuteReader();
            while (reader.Read())
                if (reader.GetInt64(0) != sourceProjectId && string.Equals(Path.GetFullPath(reader.GetString(1)), fullPath,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("This repository path is already registered as another project.");
        }
        using (var active = connection.CreateCommand())
        {
            active.Transaction = transaction;
            active.CommandText = "SELECT COUNT(*) FROM task_nodes WHERE source_project_id = $projectId AND (execution_status IN ('queued', 'clarifying', 'awaiting_clarification', 'ready_for_development', 'preparing', 'retry_worktree', 'prepared', 'running', 'committing', 'awaiting_push', 'awaiting_mr_repository_url') OR (worktree_path IS NOT NULL AND status <> 'closed'));";
            active.Parameters.AddWithValue("$projectId", sourceProjectId);
            if (Convert.ToInt32(active.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                using var existing = connection.CreateCommand();
                existing.Transaction = transaction;
                existing.CommandText = "SELECT repository_path, code_host FROM project_repositories WHERE source_project_id = $projectId;";
                existing.Parameters.AddWithValue("$projectId", sourceProjectId);
                using var existingReader = existing.ExecuteReader();
                string? saved = null;
                var savedHost = CodeHost.Gitlab;
                if (existingReader.Read())
                {
                    saved = existingReader.GetString(0);
                    savedHost = existingReader.IsDBNull(1) ? CodeHost.Gitlab : WorkflowEnumExtensions.ParseStorageString(existingReader.GetString(1), CodeHost.Gitlab);
                }
                if (saved is not null && !string.Equals(saved, fullPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                    saved is not null && savedHost != codeHost)
                    throw new InvalidOperationException("Cannot change the project repository/code host while tasks are queued or have active worktrees.");
            }
        }
        string? projectName;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT COALESCE((SELECT MIN(project) FROM task_nodes WHERE source_project_id = $projectId), (SELECT MIN(project) FROM candidates WHERE source_project_id = $projectId));";
            select.Parameters.AddWithValue("$projectId", sourceProjectId);
            projectName = select.ExecuteScalar() as string;
        }
        if (projectName is null)
        {
            transaction.Rollback();
            return null;
        }
        using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = "INSERT INTO project_repositories(source_project_id, project, repository_path, code_host, updated_at) VALUES($projectId, $project, $path, $codeHost, $updatedAt) ON CONFLICT(source_project_id) DO UPDATE SET project = excluded.project, repository_path = excluded.repository_path, code_host = excluded.code_host, updated_at = excluded.updated_at;";
            upsert.Parameters.AddWithValue("$projectId", sourceProjectId);
            upsert.Parameters.AddWithValue("$project", projectName);
            upsert.Parameters.AddWithValue("$path", fullPath);
            upsert.Parameters.AddWithValue("$codeHost", codeHost.ToStorageString());
            upsert.Parameters.AddWithValue("$updatedAt", UtcNow());
            upsert.ExecuteNonQuery();
        }
        transaction.Commit();
        return ListProjectRepositories().Single(project => project.SourceProjectId == sourceProjectId);
    }

    public ProjectRepository? SetProjectRemoteRepositoryUrl(long sourceProjectId, string remoteRepositoryUrl)
    {
        if (sourceProjectId == 0) throw new ArgumentOutOfRangeException(nameof(sourceProjectId));
        var value = remoteRepositoryUrl?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath.Trim('/') is "")
            throw new ArgumentException("Enter the HTTPS/HTTP URL of the remote repository without embedded credentials.", nameof(remoteRepositoryUrl));
        value = value.TrimEnd('/');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using (var project = connection.CreateCommand())
        {
            project.Transaction = transaction;
            project.CommandText = "UPDATE project_repositories SET remote_repository_url = $url, updated_at = $now WHERE source_project_id = $id AND code_host = 'gitlab';";
            project.Parameters.AddWithValue("$url", value);
            project.Parameters.AddWithValue("$now", UtcNow());
            project.Parameters.AddWithValue("$id", sourceProjectId);
            if (project.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                return null;
            }
        }
        using (var resume = connection.CreateCommand())
        {
            resume.Transaction = transaction;
            resume.CommandText = "UPDATE task_nodes SET execution_status = 'pushed', execution_error = NULL, updated_at = $now WHERE source_project_id = $id AND execution_status = 'awaiting_mr_repository_url' AND status <> 'closed';";
            resume.Parameters.AddWithValue("$now", UtcNow());
            resume.Parameters.AddWithValue("$id", sourceProjectId);
            resume.ExecuteNonQuery();
        }
        transaction.Commit();
        return ListProjectRepositories().SingleOrDefault(project => project.SourceProjectId == sourceProjectId);
    }

    public TaskNode? QueueTask(long id, out string? error)
    {
        error = null;
        using var connection = Open();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT t.status, t.execution_status, t.source_project_id, r.repository_path, t.worktree_path, t.merge_request_url, t.merge_request_status, t.merge_request_source_branch FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id WHERE t.id = $id;";
            check.Parameters.AddWithValue("$id", id);
            using var reader = check.ExecuteReader();
            if (!reader.Read()) return null;
            var taskStatus = reader.GetString(0);
            var executionStatus = reader.GetString(1);
            var repositoryPath = NullableString(reader, 3);
            if (taskStatus is "closed" or "done" || executionStatus == "closed")
            {
                error = "Closed or completed tasks cannot be queued.";
                return GetTaskNode(id);
            }
            if (executionStatus == "queued") return GetTaskNode(id);
            if (!reader.IsDBNull(5) && reader.GetString(6) is ("closed" or "merged"))
            {
                error = "This task is linked to a closed or merged MR. Reopen it or create a new task before queuing work.";
                return GetTaskNode(id);
            }
            if (!reader.IsDBNull(4))
            {
                error = "This task already has a saved worktree. Inspect it before starting another attempt.";
                return GetTaskNode(id);
            }
            if (executionStatus is not ("idle" or "blocked" or "failed"))
            {
                error = "This task already has an active execution.";
                return GetTaskNode(id);
            }
            if (!reader.IsDBNull(5) && reader.GetString(6) == "open" && reader.IsDBNull(7))
            {
                error = "The open MR source branch is not known yet. Configure the GitLab read MCP and retry after the MR branch is fetched.";
                return GetTaskNode(id);
            }
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                error = "Configure a local Git repository for this project before queuing the task.";
                return GetTaskNode(id);
            }
            if (!IsGitRepository(repositoryPath))
            {
                error = "The configured project repository is no longer available or is not a Git repository.";
                return GetTaskNode(id);
            }
        }

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE task_nodes SET execution_status = 'queued', build_status = 'not_started', execution_error = NULL, user_modified = 1, updated_at = $updatedAt WHERE id = $id AND status NOT IN ('closed', 'done') AND execution_status IN ('idle', 'blocked', 'failed');";
        update.Parameters.AddWithValue("$updatedAt", UtcNow());
        update.Parameters.AddWithValue("$id", id);
        if (update.ExecuteNonQuery() == 0)
        {
            error = "The task changed while it was being queued. Refresh and try again.";
            return GetTaskNode(id);
        }
        return GetTaskNode(id);
    }

    public TaskNode? RejectTask(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET status = 'closed', execution_status = 'closed', user_modified = 1, updated_at = $updatedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$updatedAt", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 0 ? null : GetTaskNode(id);
    }

    public TaskNode? TryClaimTaskPreflight()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE task_nodes SET execution_status = 'clarifying', updated_at = $now
            WHERE id = (
              SELECT t.id FROM task_nodes t
              JOIN project_repositories r ON r.source_project_id = t.source_project_id
              WHERE t.execution_status = 'queued' AND t.status NOT IN ('closed', 'done')
                AND (t.merge_request_url IS NULL OR t.merge_request_status <> 'open' OR t.merge_request_source_branch IS NOT NULL)
                AND (t.merge_request_status NOT IN ('closed', 'merged'))
              ORDER BY t.updated_at, t.id LIMIT 1
            ) AND execution_status = 'queued'
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$now", UtcNow());
        var id = command.ExecuteScalar();
        return id is null ? null : GetTaskNode(Convert.ToInt64(id, CultureInfo.InvariantCulture));
    }

    public bool SaveTaskPreflight(long taskId, TaskPreflightResult result)
    {
        ValidateTaskPreflight(result);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var task = connection.CreateCommand();
        task.Transaction = transaction;
        task.CommandText = "UPDATE task_nodes SET execution_status = 'awaiting_clarification', execution_error = NULL, updated_at = $now WHERE id = $id AND execution_status = 'clarifying' AND status <> 'closed';";
        task.Parameters.AddWithValue("$now", UtcNow());
        task.Parameters.AddWithValue("$id", taskId);
        if (task.ExecuteNonQuery() == 0)
        {
            transaction.Rollback();
            return false;
        }
        using var review = connection.CreateCommand();
        review.Transaction = transaction;
        review.CommandText = "INSERT INTO task_preflights(task_id, result_json, answers, clarification_pending, created_at) VALUES($id, $result, NULL, $pending, $now) ON CONFLICT(task_id) DO UPDATE SET result_json = excluded.result_json, clarification_pending = excluded.clarification_pending, created_at = excluded.created_at;";
        review.Parameters.AddWithValue("$id", taskId);
        review.Parameters.AddWithValue("$result", JsonSerializer.Serialize(result));
        review.Parameters.AddWithValue("$pending", result.Questions.Count > 0 ? 1 : 0);
        review.Parameters.AddWithValue("$now", UtcNow());
        review.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public TaskPreflight? GetTaskPreflight(long taskId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT result_json, answers, created_at, clarification_pending FROM task_preflights WHERE task_id = $id;";
        command.Parameters.AddWithValue("$id", taskId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var result = JsonSerializer.Deserialize<TaskPreflightResult>(reader.GetString(0))!;
        return new(taskId, result.Problem, result.ExpectedResult, result.Evidence, result.Risks,
            result.CornerCases, result.Questions, result.Decomposition, NullableString(reader, 1), reader.GetString(2), result.BuildCommand, result.BaseBranch, reader.GetBoolean(3));
    }

    public TaskPreflight? SaveTaskClarification(long taskId, string answers)
    {
        if (string.IsNullOrWhiteSpace(answers) || answers.Trim().Length > 4000)
            throw new ArgumentException("Clarification must contain 1-4000 characters.", nameof(answers));
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE task_preflights SET answers = CASE WHEN trim(COALESCE(answers, '')) = '' THEN $answers ELSE trim(answers) || char(10) || char(10) || 'Дополнительное уточнение пользователя:' || char(10) || $answers END WHERE task_id = $id AND EXISTS (SELECT 1 FROM task_nodes WHERE id = $id AND status <> 'closed' AND (execution_status IN ('awaiting_clarification', 'awaiting_development_clarification') OR execution_status = 'blocked' AND worktree_path IS NOT NULL));";
        command.Parameters.AddWithValue("$answers", answers.Trim());
        command.Parameters.AddWithValue("$id", taskId);
        if (command.ExecuteNonQuery() == 0)
        {
            transaction.Rollback();
            return null;
        }
        using (var resume = connection.CreateCommand())
        {
            resume.Transaction = transaction;
            resume.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN execution_status = 'awaiting_clarification' THEN 'queued' ELSE 'blocked' END, execution_error = NULL, updated_at = $now WHERE id = $id AND execution_status IN ('awaiting_clarification', 'awaiting_development_clarification');";
            resume.Parameters.AddWithValue("$now", UtcNow());
            resume.Parameters.AddWithValue("$id", taskId);
            resume.ExecuteNonQuery();
        }
        transaction.Commit();
        return GetTaskPreflight(taskId);
    }

    public TaskNode? ApproveTaskPreflight(long taskId, out string? error)
    {
        error = null;
        var task = GetTaskNode(taskId);
        if (task is null) return null;
        if (task.Status == "closed" || task.ExecutionStatus != TaskExecutionStatus.AwaitingClarification)
        {
            error = "Task is not awaiting pre-development approval.";
            return task;
        }
        if (!HasEstablishedAcceptanceCriteria(task.AcceptanceCriteria))
        {
            error = "Acceptance criteria are not finalized. Return the item to the project's raw queue and let the agent clarify them before development.";
            return task;
        }
        var review = GetTaskPreflight(taskId);
        if (review is null || review.QuestionsPending)
        {
            error = "Answer the open requirements questions before starting development.";
            return task;
        }
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'ready_for_development', base_branch = COALESCE(CASE WHEN merge_request_status = 'open' THEN merge_request_source_branch END, (SELECT json_extract(result_json, '$.BaseBranch') FROM task_preflights WHERE task_id = $id), base_branch), updated_at = $now WHERE id = $id AND status <> 'closed' AND execution_status = 'awaiting_clarification' AND merge_request_status NOT IN ('closed', 'merged') AND (merge_request_url IS NULL OR merge_request_status <> 'open' OR merge_request_source_branch IS NOT NULL) AND EXISTS (SELECT 1 FROM task_preflights WHERE task_id = $id AND clarification_pending = 0);";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", taskId);
        if (command.ExecuteNonQuery() == 0)
        {
            error = "Task or clarification changed. Refresh and try again.";
            return GetTaskNode(taskId);
        }
        return GetTaskNode(taskId);
    }

    public TaskNode? TryClaimWorktreePreparation()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        long id;
        string root;
        string? reservedPath;
        string? reservedBranch;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT t.id, r.repository_path, t.worktree_path, t.branch_name, t.merge_request_url, t.merge_request_status, t.merge_request_source_branch FROM task_nodes t
                JOIN project_repositories r ON r.source_project_id = t.source_project_id
                WHERE ((t.execution_status = 'ready_for_development' AND t.worktree_path IS NULL)
                  OR (t.execution_status = 'retry_worktree' AND t.worktree_path IS NOT NULL))
                  AND (t.merge_request_url IS NULL OR t.merge_request_status <> 'open' OR t.merge_request_source_branch IS NOT NULL)
                  AND t.merge_request_status NOT IN ('closed', 'merged')
                  AND t.status NOT IN ('closed', 'done') ORDER BY t.updated_at, t.id LIMIT 1;
                """;
            using var reader = select.ExecuteReader();
            if (!reader.Read())
            {
                transaction.Commit();
                return null;
            }
            id = reader.GetInt64(0);
            root = reader.GetString(1);
            reservedPath = NullableString(reader, 2);
            reservedBranch = NullableString(reader, 3);
            if (!reader.IsDBNull(4) && reader.GetString(5) == "open")
                reservedBranch = reader.GetString(6);
        }
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = reservedPath ?? Path.Combine(Path.GetDirectoryName(normalizedRoot)!, $"{Path.GetFileName(normalizedRoot)}-todo-graph-{id}-{suffix}");
        var branch = reservedBranch ?? $"todo-graph/task-{id}-{suffix}";
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE task_nodes SET execution_status = 'preparing', worktree_path = $path, branch_name = $branch, execution_error = NULL, updated_at = $now WHERE id = $id AND execution_status IN ('ready_for_development', 'retry_worktree');";
            update.Parameters.AddWithValue("$path", path);
            update.Parameters.AddWithValue("$branch", branch);
            update.Parameters.AddWithValue("$now", UtcNow());
            update.Parameters.AddWithValue("$id", id);
            if (update.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                return null;
            }
        }
        transaction.Commit();
        return GetTaskNode(id);
    }

    public TaskNode? RetryWorktreePreparation(long id, out string? error)
    {
        error = null;
        var task = GetTaskNode(id);
        if (task is null) return null;
        if (task.ExecutionStatus != TaskExecutionStatus.Blocked || task.Status is "closed" or "done" || string.IsNullOrWhiteSpace(task.WorktreePath))
        {
            error = "Only a blocked worktree preparation can be retried.";
            return task;
        }
        if (Directory.Exists(task.WorktreePath) || File.Exists(task.WorktreePath))
        {
            error = "The reserved worktree exists. Inspect it before retrying; no files will be deleted.";
            return task;
        }
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'retry_worktree', execution_error = NULL, updated_at = $now WHERE id = $id AND status NOT IN ('closed', 'done') AND execution_status = 'blocked' AND worktree_path = $path;";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$path", task.WorktreePath);
        if (command.ExecuteNonQuery() == 0)
        {
            error = "The task changed. Refresh and retry.";
            return GetTaskNode(id);
        }
        return GetTaskNode(id);
    }

    public bool FinishWorktreePreparation(long taskId, string baseBranch)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'prepared', base_branch = $base, execution_error = NULL, updated_at = $now WHERE id = $id AND status <> 'closed' AND execution_status = 'preparing';";
        command.Parameters.AddWithValue("$base", baseBranch);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", taskId);
        return command.ExecuteNonQuery() > 0;
    }

    public void FailWorktreePreparation(long taskId, string error)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'blocked', execution_error = $error, updated_at = $now WHERE id = $id AND execution_status = 'preparing';";
        command.Parameters.AddWithValue("$error", error.Length > 2000 ? error[..2000] : error);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", taskId);
        command.ExecuteNonQuery();
    }

    public TaskNode? TryClaimImplementation()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'running', build_status = 'not_started', execution_error = NULL, updated_at = $now WHERE id = (SELECT id FROM task_nodes WHERE execution_status IN ('prepared', 'reviewing_comments') AND status NOT IN ('closed', 'done') AND merge_request_status NOT IN ('closed', 'merged') AND worktree_path IS NOT NULL ORDER BY updated_at, id LIMIT 1) AND execution_status IN ('prepared', 'reviewing_comments') RETURNING id;";
        command.Parameters.AddWithValue("$now", UtcNow());
        var result = command.ExecuteScalar();
        return result is null ? null : GetTaskNode(Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public void SetTaskBuildState(long id, ProjectBuildStatus status, string? error = null)
    {
        if (status is not (ProjectBuildStatus.Running or ProjectBuildStatus.Failed or ProjectBuildStatus.Passed))
            throw new ArgumentException("Invalid task build status.", nameof(status));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET build_status = $status, execution_error = $error, updated_at = $now WHERE id = $id AND execution_status IN ('running', 'committing');";
        command.Parameters.AddWithValue("$status", status.ToStorageString());
        command.Parameters.AddWithValue("$error", Db(error is null ? null : error.Length > 2000 ? error[..2000] : error));
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public bool InvalidateTaskBuildForRebase(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET build_status = 'not_started', execution_error = NULL, updated_at = $now WHERE id = $id AND execution_status = 'committing';";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public bool CompleteImplementation(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN execution_status = 'comments_reviewed' THEN 'comments_reviewed' ELSE 'awaiting_commit' END, build_status = 'passed', execution_error = NULL, updated_at = $now WHERE id = $id AND status <> 'closed' AND merge_request_status NOT IN ('closed', 'merged') AND execution_status IN ('running', 'comments_reviewed') AND build_status = 'passed';";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public void BlockImplementation(long id, string error)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'blocked', execution_error = $error, updated_at = $now WHERE id = $id AND execution_status = 'running';";
        command.Parameters.AddWithValue("$error", error.Length > 2000 ? error[..2000] : error);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void AwaitDevelopmentClarification(long id, string question)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'awaiting_development_clarification', execution_error = $question, updated_at = $now WHERE id = $id AND execution_status = 'running';";
        command.Parameters.AddWithValue("$question", question.Length > 2000 ? question[..2000] : question);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public TaskNode? ResumeImplementation(long id, out string? error)
    {
        error = null;
        var task = GetTaskNode(id);
        if (task is null) return null;
        if (task.ExecutionStatus != TaskExecutionStatus.Blocked || task.Status is "closed" or "done" ||
            string.IsNullOrWhiteSpace(task.WorktreePath) || !Directory.Exists(task.WorktreePath))
        {
            error = "Only a blocked task with its existing worktree can resume development.";
            return task;
        }
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'prepared', execution_error = NULL, updated_at = $now WHERE id = $id AND execution_status = 'blocked' AND status NOT IN ('closed', 'done') AND worktree_path = $path;";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$path", task.WorktreePath);
        if (command.ExecuteNonQuery() == 0) error = "Task changed while resuming development.";
        return GetTaskNode(id);
    }

    public TaskNode? TryClaimTaskCommit()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'committing', updated_at = $now WHERE id = (SELECT id FROM task_nodes WHERE execution_status IN ('awaiting_commit', 'comments_reviewed') AND build_status = 'passed' AND status NOT IN ('closed', 'done') AND merge_request_status NOT IN ('closed', 'merged') AND worktree_path IS NOT NULL ORDER BY updated_at, id LIMIT 1) AND execution_status IN ('awaiting_commit', 'comments_reviewed') RETURNING id;";
        command.Parameters.AddWithValue("$now", UtcNow());
        var result = command.ExecuteScalar();
        return result is null ? null : GetTaskNode(Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public bool RecordTaskCommit(long id, string sha)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(sha, "^[a-fA-F0-9]{40,64}$"))
            throw new ArgumentException("Invalid Git commit SHA.", nameof(sha));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN (SELECT code_host FROM project_repositories WHERE source_project_id = task_nodes.source_project_id) = 'github' THEN 'awaiting_user_pr' ELSE 'awaiting_push' END, commit_sha = $sha, execution_error = NULL, updated_at = $now WHERE id = $id AND status <> 'closed' AND merge_request_status NOT IN ('closed', 'merged') AND execution_status = 'committing' AND build_status = 'passed';";
        command.Parameters.AddWithValue("$sha", sha);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public void FailTaskCommit(long id, string error)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'blocked', execution_error = $error, updated_at = $now WHERE id = $id AND execution_status = 'committing';";
        command.Parameters.AddWithValue("$error", error.Length > 2000 ? error[..2000] : error);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TaskNode> ListAwaitingPush()
    {
        // The task list is small in this local application; use the existing mapper to keep status projection consistent.
        return ListTaskNodes().Where(task => task.ExecutionStatus == TaskExecutionStatus.AwaitingPush && task.CommitSha is not null && task.Status != "closed").ToArray();
    }

    public bool RecordVerifiedPush(long id, string sha)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN (SELECT code_host FROM project_repositories WHERE source_project_id = task_nodes.source_project_id) = 'github' THEN 'awaiting_user_pr' WHEN (SELECT remote_repository_url FROM project_repositories WHERE source_project_id = task_nodes.source_project_id) IS NULL THEN 'awaiting_mr_repository_url' ELSE 'pushed' END, execution_error = NULL, updated_at = $now WHERE id = $id AND execution_status = 'awaiting_push' AND status <> 'closed' AND commit_sha = $sha;";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$sha", sha);
        return command.ExecuteNonQuery() > 0;
    }

    public void ReuseOpenMergeRequests()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN EXISTS (SELECT 1 FROM merge_request_comments c WHERE c.task_id = task_nodes.id AND c.merge_request_url = task_nodes.merge_request_url AND c.agent_handled_at IS NULL AND NOT EXISTS (SELECT 1 FROM merge_request_comments own WHERE own.task_id = c.task_id AND own.reply_external_id = c.external_comment_id)) THEN 'reviewing_comments' ELSE 'code_review' END, updated_at = $now WHERE execution_status = 'pushed' AND status <> 'closed' AND merge_request_status = 'open' AND merge_request_url IS NOT NULL;";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.ExecuteNonQuery();
    }

    public void BlockInactiveMergeRequests()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'mr_retry_required', execution_error = 'The linked MR is closed or merged. Decide whether to reopen it or create a new MR before continuing.' WHERE execution_status = 'pushed' AND status <> 'closed' AND merge_request_url IS NOT NULL AND merge_request_status <> 'open';";
        command.ExecuteNonQuery();
    }

    public TaskNode? TryClaimMergeRequestCreation()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'creating_mr', updated_at = $now WHERE id = (SELECT t.id FROM task_nodes t JOIN project_repositories r ON r.source_project_id = t.source_project_id WHERE t.execution_status = 'pushed' AND t.status <> 'closed' AND r.code_host = 'gitlab' AND t.commit_sha IS NOT NULL AND t.branch_name IS NOT NULL AND t.merge_request_url IS NULL ORDER BY t.updated_at, t.id LIMIT 1) AND execution_status = 'pushed' RETURNING id;";
        command.Parameters.AddWithValue("$now", UtcNow());
        var result = command.ExecuteScalar();
        return result is null ? null : GetTaskNode(Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public bool RecordCreatedMergeRequest(long id, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || url.Length > 2048)
            throw new ArgumentException("MR creator returned an invalid URL.", nameof(url));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET merge_request_url = $url, merge_request_status = 'open', execution_status = 'code_review', execution_error = NULL, user_modified = 1, updated_at = $now WHERE id = $id AND status <> 'closed' AND execution_status = 'creating_mr' AND merge_request_url IS NULL;";
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public void FailMergeRequestCreation(long id, string message)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'mr_retry_required', execution_error = $error, updated_at = $now WHERE id = $id AND execution_status = 'creating_mr';";
        command.Parameters.AddWithValue("$error", message.Length > 2000 ? message[..2000] : message);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public TaskNode? RetryMergeRequestCreation(long id, out string? error)
    {
        error = null;
        var task = GetTaskNode(id);
        if (task is null) return null;
        if (task.ExecutionStatus != TaskExecutionStatus.MrRetryRequired || task.Status == "closed" || task.CommitSha is null ||
            task.BranchName is null || task.MergeRequestUrl is not null && task.MergeRequestStatus != MergeRequestStatus.Open)
        {
            error = "Check the linked MR and remote branch before retrying MR creation.";
            return task;
        }
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN merge_request_url IS NOT NULL THEN 'code_review' WHEN (SELECT remote_repository_url FROM project_repositories WHERE source_project_id = task_nodes.source_project_id) IS NULL THEN 'awaiting_mr_repository_url' ELSE 'pushed' END, execution_error = NULL, updated_at = $now WHERE id = $id AND status <> 'closed' AND execution_status = 'mr_retry_required' AND commit_sha = $sha AND (merge_request_url IS NULL OR merge_request_status = 'open');";
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$sha", task.CommitSha);
        if (command.ExecuteNonQuery() == 0) error = "Task changed while retrying MR creation.";
        return GetTaskNode(id);
    }

    public void FailTaskPreflight(long taskId, string message)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = 'failed', execution_error = $error, updated_at = $now WHERE id = $id AND execution_status = 'clarifying';";
        command.Parameters.AddWithValue("$error", message.Length > 2000 ? message[..2000] : message);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", taskId);
        command.ExecuteNonQuery();
    }

    private static void ValidateTaskPreflight(TaskPreflightResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Problem) || string.IsNullOrWhiteSpace(result.ExpectedResult) ||
            result.Problem.Length > 4000 || result.ExpectedResult.Length > 4000 ||
            result.Evidence is null || result.Risks is null || result.CornerCases is null || result.Questions is null ||
            result.Evidence.Count > 20 || result.Risks.Count > 20 || result.CornerCases.Count > 20 || result.Questions.Count > 20 ||
            new[] { result.Evidence, result.Risks, result.CornerCases, result.Questions }.SelectMany(items => items)
                .Any(item => string.IsNullOrWhiteSpace(item) || item.Length > 2000) ||
            result.Decomposition?.Length > 4000 || result.BuildCommand?.Length > 1000 || result.BaseBranch?.Length > 300)
            throw new ArgumentException("Task explanation must contain valid, bounded text and lists.", nameof(result));
    }

    private static TaskNode MapTaskNode(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.IsDBNull(1) ? null : reader.GetInt64(1),
        NullableString(reader, 2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        NullableString(reader, 7),
        reader.GetString(8),
        JsonSerializer.Deserialize<IReadOnlyList<string>>(reader.GetString(9)) ?? [],
        reader.GetBoolean(10),
        reader.GetString(11),
        reader.GetString(12),
        NullableString(reader, 13),
        WorkflowEnumExtensions.ParseStorageString(reader.GetString(14), TaskExecutionStatus.Idle),
        WorkflowEnumExtensions.ParseStorageString(reader.GetString(15), ProjectBuildStatus.NotStarted),
        NullableString(reader, 16),
        NullableString(reader, 17),
        NullableString(reader, 18),
        NullableString(reader, 19),
        NullableString(reader, 20),
        WorkflowEnumExtensions.ParseStorageString(reader.GetString(21), MergeRequestStatus.None),
        NullableString(reader, 22),
        NullableString(reader, 23),
        NullableString(reader, 24),
        WorkflowEnumExtensions.ParseStorageString(reader.GetString(25), CodeHost.Gitlab),
        WorkflowEnumExtensions.ParseStorageString(reader.GetString(26), MergeApprovalState.Unknown),
        WorkflowEnumExtensions.ParseStorageString(reader.GetString(27), MergeabilityState.Unknown),
        reader.IsDBNull(28) ? null : reader.GetInt32(28), reader.IsDBNull(29) ? null : reader.GetInt32(29), NullableString(reader, 30), NullableString(reader, 31));

    public TaskNode? UpdateTask(long id, TaskUpdateRequest request)
    {
        var mergeRequestUrl = string.IsNullOrWhiteSpace(request.MergeRequestUrl) ? null : request.MergeRequestUrl.Trim();
        if (mergeRequestUrl is not null &&
            (mergeRequestUrl.Length > 2048 || !Uri.TryCreate(mergeRequestUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new ArgumentException("Merge request URL must be an absolute HTTP(S) URL without embedded credentials, up to 2048 characters.", nameof(request));
        if (request.MergeRequestStatus is not null && !Enum.IsDefined(request.MergeRequestStatus.Value) ||
            mergeRequestUrl is null && request.MergeRequestStatus is not (null or MergeRequestStatus.None) ||
            mergeRequestUrl is not null && request.MergeRequestStatus == MergeRequestStatus.None)
            throw new ArgumentException("MR status must be none without a URL, or open, closed or merged with a URL.", nameof(request));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET merge_request_url = $mergeRequestUrl, merge_request_status = CASE WHEN $mergeRequestUrl IS NULL THEN 'none' WHEN $mrStatus IS NOT NULL THEN $mrStatus WHEN merge_request_url = $mergeRequestUrl AND merge_request_status <> 'none' THEN merge_request_status ELSE 'open' END, merge_request_source_branch = CASE WHEN merge_request_url IS $mergeRequestUrl OR merge_request_url = $mergeRequestUrl THEN merge_request_source_branch ELSE NULL END, merge_request_target_branch = CASE WHEN merge_request_url IS $mergeRequestUrl OR merge_request_url = $mergeRequestUrl THEN merge_request_target_branch ELSE NULL END, approval_state = CASE WHEN merge_request_url IS $mergeRequestUrl OR merge_request_url = $mergeRequestUrl THEN approval_state ELSE 'unknown' END, mergeability = CASE WHEN merge_request_url IS $mergeRequestUrl OR merge_request_url = $mergeRequestUrl THEN mergeability ELSE 'unknown' END, approved_count = CASE WHEN merge_request_url IS $mergeRequestUrl OR merge_request_url = $mergeRequestUrl THEN approved_count ELSE NULL END, required_approvals = CASE WHEN merge_request_url IS $mergeRequestUrl OR merge_request_url = $mergeRequestUrl THEN required_approvals ELSE NULL END, execution_status = CASE WHEN execution_status = 'awaiting_user_pr' AND $mergeRequestUrl IS NOT NULL THEN 'code_review' ELSE execution_status END, user_modified = 1, updated_at = $updatedAt WHERE id = $id AND status <> 'closed' AND ((worktree_path IS NULL AND execution_status NOT IN ('queued', 'clarifying', 'awaiting_clarification', 'ready_for_development', 'preparing', 'retry_worktree', 'prepared', 'running', 'committing', 'awaiting_commit', 'awaiting_push', 'pushed', 'creating_mr', 'reviewing_comments', 'comments_reviewed')) OR (execution_status = 'awaiting_user_pr' AND merge_request_url IS NULL AND $mergeRequestUrl IS NOT NULL));";
        command.Parameters.AddWithValue("$mergeRequestUrl", Db(mergeRequestUrl));
        command.Parameters.AddWithValue("$mrStatus", Db(request.MergeRequestStatus?.ToStorageString()));
        command.Parameters.AddWithValue("$updatedAt", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 0 ? null : GetTaskNode(id);
    }

    public TaskNode? UpdateMergeRequestStatus(long id, MergeRequestStatus status)
    {
        if (status is not (MergeRequestStatus.Open or MergeRequestStatus.Closed or MergeRequestStatus.Merged))
            throw new ArgumentException("MR status must be open, closed or merged.", nameof(status));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET merge_request_status = $status, user_modified = 1, updated_at = $now WHERE id = $id AND status <> 'closed' AND merge_request_url IS NOT NULL;";
        command.Parameters.AddWithValue("$status", status.ToStorageString());
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 0 ? null : GetTaskNode(id);
    }

    public IReadOnlyList<TaskNode> ListPollableMergeRequests()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT t.id, t.source_project_id, t.source_task_id, t.project, t.title, t.description, t.acceptance_criteria, t.current_consequences, t.status, t.source_report_ids_json, t.user_modified, t.created_at, t.updated_at, t.merge_request_url, t.execution_status, t.build_status, r.repository_path, t.execution_error, t.worktree_path, t.branch_name, t.base_branch, t.merge_request_status, t.commit_sha, t.merge_request_source_branch, t.created_by, COALESCE(r.code_host, 'gitlab'), t.approval_state, t.mergeability, t.approved_count, t.required_approvals, t.merge_request_target_branch, r.remote_repository_url FROM task_nodes t LEFT JOIN project_repositories r ON r.source_project_id = t.source_project_id WHERE t.status <> 'closed' AND t.merge_request_status = 'open' AND t.merge_request_url IS NOT NULL ORDER BY t.updated_at, t.id;";
        using var reader = command.ExecuteReader();
        var tasks = new List<TaskNode>();
        while (reader.Read()) tasks.Add(MapTaskNode(reader));
        return tasks;
    }

    public bool SaveMergeRequestComments(long taskId, string mergeRequestUrl, IReadOnlyList<MergeRequestCommentInput> comments)
    {
        if (comments.Count > 1000) throw new ArgumentException("A poll result cannot contain more than 1000 comments.", nameof(comments));
        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Id) || comment.Id.Length > 200 ||
                string.IsNullOrWhiteSpace(comment.DiscussionId) || comment.DiscussionId.Length > 200 ||
                string.IsNullOrWhiteSpace(comment.Body) || comment.Body.Length > 20000 ||
                string.IsNullOrWhiteSpace(comment.Author) || comment.Author.Length > 500 ||
                string.IsNullOrWhiteSpace(comment.CreatedAt) || comment.CreatedAt.Length > 100 ||
                (comment.Url is not null && (comment.Url.Length > 2048 || !IsHttpUrl(comment.Url))))
                throw new ArgumentException("MR comment data is invalid.", nameof(comments));
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using (var eligibility = connection.CreateCommand())
        {
            eligibility.Transaction = transaction;
            eligibility.CommandText = "SELECT 1 FROM task_nodes WHERE id = $id AND status <> 'closed' AND merge_request_status = 'open' AND merge_request_url = $url;";
            eligibility.Parameters.AddWithValue("$id", taskId);
            eligibility.Parameters.AddWithValue("$url", mergeRequestUrl);
            if (eligibility.ExecuteScalar() is null)
            {
                transaction.Rollback();
                return false;
            }
        }

        var now = UtcNow();
        var newExternalComments = false;
        foreach (var comment in comments)
        {
            using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT 1 FROM merge_request_comments WHERE task_id = $taskId AND merge_request_url = $mrUrl AND (external_comment_id = $id OR reply_external_id = $id) LIMIT 1;";
                existing.Parameters.AddWithValue("$taskId", taskId);
                existing.Parameters.AddWithValue("$mrUrl", mergeRequestUrl);
                existing.Parameters.AddWithValue("$id", comment.Id);
                if (existing.ExecuteScalar() is null) newExternalComments = true;
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO merge_request_comments(task_id, merge_request_url, external_comment_id, discussion_id, body, author, comment_created_at, comment_url, observed_at) VALUES($taskId, $mrUrl, $id, $discussionId, $body, $author, $createdAt, $url, $observedAt) ON CONFLICT(task_id, merge_request_url, external_comment_id) DO UPDATE SET discussion_id = excluded.discussion_id, body = excluded.body, author = excluded.author, comment_created_at = excluded.comment_created_at, comment_url = excluded.comment_url, observed_at = excluded.observed_at;";
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$mrUrl", mergeRequestUrl);
            command.Parameters.AddWithValue("$id", comment.Id);
            command.Parameters.AddWithValue("$discussionId", comment.DiscussionId);
            command.Parameters.AddWithValue("$body", comment.Body);
            command.Parameters.AddWithValue("$author", comment.Author);
            command.Parameters.AddWithValue("$createdAt", comment.CreatedAt);
            command.Parameters.AddWithValue("$url", Db(comment.Url));
            command.Parameters.AddWithValue("$observedAt", now);
            command.ExecuteNonQuery();
        }
        if (newExternalComments)
        {
            using var queue = connection.CreateCommand();
            queue.Transaction = transaction;
            queue.CommandText = "UPDATE task_nodes SET execution_status = 'reviewing_comments', updated_at = $now WHERE id = $taskId AND execution_status IN ('code_review', 'comments_reviewed') AND worktree_path IS NOT NULL AND merge_request_status = 'open' AND merge_request_url = $mrUrl;";
            queue.Parameters.AddWithValue("$now", now);
            queue.Parameters.AddWithValue("$taskId", taskId);
            queue.Parameters.AddWithValue("$mrUrl", mergeRequestUrl);
            queue.ExecuteNonQuery();
        }
        transaction.Commit();
        return true;
    }

    public bool SaveMergeRequestReview(long taskId, string mergeRequestUrl, MergeRequestPollResult review)
    {
        if (review.Status is not (MergeRequestStatus.Open or MergeRequestStatus.Closed or MergeRequestStatus.Merged) ||
            !IsValidBranchName(review.SourceBranch) || review.MergeRequestTargetBranch is null || !IsValidBranchName(review.MergeRequestTargetBranch))
            throw new ArgumentException("MR state, source branch or target branch is invalid.", nameof(review));
        if (review.Status == MergeRequestStatus.Open) SaveMergeRequestComments(taskId, mergeRequestUrl, review.Comments);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET merge_request_status = $status, merge_request_source_branch = $branch, merge_request_target_branch = $targetBranch, approval_state = $approvalState, mergeability = $mergeability, approved_count = $approvedCount, required_approvals = $requiredApprovals, execution_status = CASE WHEN $status <> 'open' AND execution_status IN ('reviewing_comments', 'running', 'comments_reviewed', 'awaiting_commit') THEN 'blocked' WHEN $status = 'open' AND worktree_path IS NOT NULL AND branch_name <> $branch THEN 'blocked' ELSE execution_status END, execution_error = CASE WHEN $status <> 'open' AND execution_status IN ('reviewing_comments', 'running', 'comments_reviewed', 'awaiting_commit') THEN 'Связанный MR/PR закрыт или слит. Проверьте его состояние перед продолжением.' WHEN $status = 'open' AND worktree_path IS NOT NULL AND branch_name <> $branch THEN 'Source branch связанного MR/PR не совпадает с веткой worktree. Исполнитель остановлен, чтобы не изменить другую ветку.' ELSE execution_error END, user_modified = 1, updated_at = $now WHERE id = $id AND merge_request_url = $url AND status <> 'closed';";
        command.Parameters.AddWithValue("$status", review.Status.ToStorageString());
        command.Parameters.AddWithValue("$branch", review.SourceBranch);
        command.Parameters.AddWithValue("$targetBranch", Db(review.MergeRequestTargetBranch));
        command.Parameters.AddWithValue("$approvalState", review.ApprovalState.ToStorageString());
        command.Parameters.AddWithValue("$mergeability", review.Mergeability.ToStorageString());
        command.Parameters.AddWithValue("$approvedCount", review.ApprovedCount is null ? DBNull.Value : review.ApprovedCount.Value);
        command.Parameters.AddWithValue("$requiredApprovals", review.RequiredApprovals is null ? DBNull.Value : review.RequiredApprovals.Value);
        command.Parameters.AddWithValue("$now", UtcNow());
        command.Parameters.AddWithValue("$id", taskId);
        command.Parameters.AddWithValue("$url", mergeRequestUrl);
        return command.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<MergeRequestComment> ListMergeRequestComments(long taskId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.task_id, c.merge_request_url, c.external_comment_id, c.discussion_id, c.body, c.author, c.comment_created_at, c.comment_url, c.observed_at, c.reviewed_at, c.reply_body, c.reply_status, c.reply_error, c.reply_external_id, c.reply_sent_at, c.agent_handled_at FROM merge_request_comments c JOIN task_nodes t ON t.id = c.task_id AND t.merge_request_url = c.merge_request_url WHERE c.task_id = $taskId ORDER BY c.comment_created_at, c.external_comment_id;";
        command.Parameters.AddWithValue("$taskId", taskId);
        using var reader = command.ExecuteReader();
        var comments = new List<MergeRequestComment>();
        while (reader.Read())
            comments.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), NullableString(reader, 7), reader.GetString(8), NullableString(reader, 9), NullableString(reader, 10), NullableString(reader, 11), NullableString(reader, 12), NullableString(reader, 13), NullableString(reader, 14), NullableString(reader, 15)));
        return comments;
    }

    public IReadOnlyList<MergeRequestComment> ListPendingReviewComments(long taskId)
    {
        var comments = ListMergeRequestComments(taskId);
        var ownReplyIds = comments.Where(comment => comment.ReplyExternalId is not null)
            .Select(comment => comment.ReplyExternalId!).ToHashSet(StringComparer.Ordinal);
        return comments.Where(comment => comment.AgentHandledAt is null && !ownReplyIds.Contains(comment.Id)).ToArray();
    }

    public void MarkReviewCommentsHandled(long taskId, string mergeRequestUrl, IReadOnlyList<string> ids)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE merge_request_comments SET agent_handled_at = COALESCE(agent_handled_at, $now) WHERE task_id = $taskId AND merge_request_url = $url AND external_comment_id = $id;";
            command.Parameters.AddWithValue("$now", UtcNow());
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$url", mergeRequestUrl);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
        if (ids.Count > 0)
        {
            using var task = connection.CreateCommand();
            task.Transaction = transaction;
            task.CommandText = "UPDATE task_nodes SET execution_status = 'comments_reviewed', updated_at = $now WHERE id = $taskId AND execution_status = 'running' AND merge_request_status = 'open' AND merge_request_url = $url;";
            task.Parameters.AddWithValue("$now", UtcNow());
            task.Parameters.AddWithValue("$taskId", taskId);
            task.Parameters.AddWithValue("$url", mergeRequestUrl);
            task.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public bool CompleteReplyOnlyReview(long id, string mergeRequestUrl, IReadOnlyList<string> commentIds)
    {
        MarkReviewCommentsHandled(id, mergeRequestUrl, commentIds);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE task_nodes SET execution_status = CASE WHEN EXISTS (SELECT 1 FROM merge_request_comments c WHERE c.task_id = $id AND c.merge_request_url = $url AND c.agent_handled_at IS NULL AND NOT EXISTS (SELECT 1 FROM merge_request_comments own WHERE own.task_id = c.task_id AND own.reply_external_id = c.external_comment_id)) THEN 'reviewing_comments' ELSE 'comments_reviewed' END, build_status = 'not_started', execution_error = NULL, updated_at = $now WHERE id = $id AND status <> 'closed' AND execution_status IN ('running', 'comments_reviewed') AND merge_request_status = 'open' AND merge_request_url = $url;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$url", mergeRequestUrl);
        command.Parameters.AddWithValue("$now", UtcNow());
        return command.ExecuteNonQuery() > 0;
    }

    public MergeRequestComment? SaveMergeRequestReplyDraft(long taskId, string commentId, string body)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrWhiteSpace(commentId) || commentId.Length > 200)
            throw new ArgumentException("A valid comment ID is required.", nameof(commentId));
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 10000)
            throw new ArgumentException("Reply text must contain 1 to 10000 characters.", nameof(body));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE merge_request_comments SET reply_body = $body, reply_status = 'draft', reply_error = NULL, reply_external_id = NULL, reply_sent_at = NULL WHERE task_id = $taskId AND external_comment_id = $commentId AND merge_request_url = (SELECT t.merge_request_url FROM task_nodes t JOIN project_repositories p ON p.source_project_id = t.source_project_id WHERE t.id = $taskId AND t.status <> 'closed' AND t.merge_request_status = 'open' AND p.code_host = 'gitlab') AND (reply_status IS NULL OR reply_status IN ('draft', 'failed'));";
        command.Parameters.AddWithValue("$body", trimmed);
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$commentId", commentId);
        if (command.ExecuteNonQuery() == 0) return null;
        return ListMergeRequestComments(taskId).SingleOrDefault(comment => comment.Id == commentId);
    }

    public bool ConfirmMergeRequestReply(long taskId, string commentId)
    {
        if (string.IsNullOrWhiteSpace(commentId) || commentId.Length > 200) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE merge_request_comments SET reply_status = 'queued', reply_error = NULL WHERE task_id = $taskId AND external_comment_id = $commentId AND reply_status = 'draft' AND reply_body IS NOT NULL AND merge_request_url = (SELECT t.merge_request_url FROM task_nodes t JOIN project_repositories p ON p.source_project_id = t.source_project_id WHERE t.id = $taskId AND t.status <> 'closed' AND t.merge_request_status = 'open' AND p.code_host = 'gitlab');";
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$commentId", commentId);
        return command.ExecuteNonQuery() > 0;
    }

    public MergeRequestReply? TryClaimMergeRequestReply()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        MergeRequestReply? reply;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT c.task_id, c.merge_request_url, c.external_comment_id, c.discussion_id, c.body, c.reply_body, t.project, t.title FROM merge_request_comments c JOIN task_nodes t ON t.id = c.task_id AND t.merge_request_url = c.merge_request_url JOIN project_repositories p ON p.source_project_id = t.source_project_id WHERE c.reply_status = 'queued' AND t.status <> 'closed' AND t.merge_request_status = 'open' AND p.code_host = 'gitlab' ORDER BY c.observed_at, c.task_id LIMIT 1;";
            using var reader = select.ExecuteReader();
            if (!reader.Read())
            {
                transaction.Commit();
                return null;
            }
            reply = new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));
        }
        using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = "UPDATE merge_request_comments SET reply_status = 'sending' WHERE task_id = $taskId AND merge_request_url = $mrUrl AND external_comment_id = $commentId AND reply_status = 'queued';";
            claim.Parameters.AddWithValue("$taskId", reply.TaskId);
            claim.Parameters.AddWithValue("$mrUrl", reply.MergeRequestUrl);
            claim.Parameters.AddWithValue("$commentId", reply.CommentId);
            if (claim.ExecuteNonQuery() == 0)
            {
                transaction.Rollback();
                return null;
            }
        }
        transaction.Commit();
        return reply;
    }

    public bool MarkMergeRequestReplySent(MergeRequestReply reply, string externalReplyId)
    {
        if (string.IsNullOrWhiteSpace(externalReplyId) || externalReplyId.Length > 200) return false;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE merge_request_comments SET reply_status = 'sent', reply_external_id = $replyId, reply_sent_at = $sentAt, reply_error = NULL WHERE task_id = $taskId AND merge_request_url = $mrUrl AND external_comment_id = $commentId AND reply_status = 'sending';";
        command.Parameters.AddWithValue("$replyId", externalReplyId);
        command.Parameters.AddWithValue("$sentAt", UtcNow());
        command.Parameters.AddWithValue("$taskId", reply.TaskId);
        command.Parameters.AddWithValue("$mrUrl", reply.MergeRequestUrl);
        command.Parameters.AddWithValue("$commentId", reply.CommentId);
        return command.ExecuteNonQuery() > 0;
    }

    public bool MarkMergeRequestReplyFailed(MergeRequestReply reply, string error)
    {
        var safeError = string.IsNullOrWhiteSpace(error) ? "Reply delivery could not be confirmed. Verify GitLab before resubmitting." : error;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE merge_request_comments SET reply_status = 'failed', reply_error = $error WHERE task_id = $taskId AND merge_request_url = $mrUrl AND external_comment_id = $commentId AND reply_status = 'sending';";
        command.Parameters.AddWithValue("$error", safeError.Length > 2000 ? safeError[..2000] : safeError);
        command.Parameters.AddWithValue("$taskId", reply.TaskId);
        command.Parameters.AddWithValue("$mrUrl", reply.MergeRequestUrl);
        command.Parameters.AddWithValue("$commentId", reply.CommentId);
        return command.ExecuteNonQuery() > 0;
    }

    public MergeRequestComment? MarkMergeRequestCommentReviewed(long taskId, string commentId)
    {
        if (string.IsNullOrWhiteSpace(commentId) || commentId.Length > 200) return null;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE merge_request_comments SET reviewed_at = COALESCE(reviewed_at, $reviewedAt) WHERE task_id = $taskId AND external_comment_id = $commentId AND merge_request_url = (SELECT merge_request_url FROM task_nodes WHERE id = $taskId);";
        command.Parameters.AddWithValue("$reviewedAt", UtcNow());
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$commentId", commentId);
        if (command.ExecuteNonQuery() == 0) return null;
        return ListMergeRequestComments(taskId).SingleOrDefault(comment => comment.Id == commentId);
    }

    public IReadOnlyList<Candidate> List(string? status = null, long? projectId = null)
    {
        if (status is not null && status is not ("pending" or "triaged" or "dismissed"))
            throw new ArgumentException("Unsupported triage status.", nameof(status));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = status is null
            ? "SELECT id FROM candidates WHERE ($projectId IS NULL OR source_project_id = $projectId) ORDER BY CASE triage_status WHEN 'pending' THEN 0 ELSE 1 END, id DESC;"
            : "SELECT id FROM candidates WHERE triage_status = $status AND ($projectId IS NULL OR source_project_id = $projectId) ORDER BY id DESC;";
        if (status is not null) command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$projectId", projectId is null ? DBNull.Value : projectId.Value);
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read()) ids.Add(reader.GetInt64(0));
        reader.Close();
        return ids.Select(id => Get(id)!).ToArray();
    }

    public Candidate? Get(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, source_report_id, project, title, observation, location, impact, suggested_action, discovered_during, source_status, source_created_at, received_at, source_url, triage_status, localized_title, localized_description, source_project_id, created_by, source, raw_text FROM candidates WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var candidate = new Candidate(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            NullableString(reader, 5), NullableString(reader, 6), NullableString(reader, 7), NullableString(reader, 8),
            reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), [], [],
            LocalizedTitle: NullableString(reader, 14), LocalizedDescription: NullableString(reader, 15),
            SourceProjectId: reader.IsDBNull(16) ? null : reader.GetInt64(16),
            CreatedBy: NullableString(reader, 17), Source: reader.GetString(18), RawText: NullableString(reader, 19));
        reader.Close();
        return candidate with
        {
            Links = GetLinks(connection, id),
            SuggestedLinks = GetSuggestions(connection, id),
            AgentStatus = GetAgentJob(id)?.Status ?? "queued",
            ClarificationRounds = ListCandidateClarificationRounds(id),
        };
    }

    public Candidate? SubmitRawItem(long projectId, string text, string createdBy)
    {
        var rawText = text?.Trim();
        if (string.IsNullOrWhiteSpace(rawText) || rawText.Length > 10000)
            throw new ArgumentException("Raw text must contain 1-10000 characters.", nameof(text));
        var author = string.IsNullOrWhiteSpace(createdBy) ? Environment.UserName : createdBy.Trim();
        if (author.Length > 100) throw new ArgumentException("Creator name is limited to 100 characters.", nameof(createdBy));
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        string? projectName;
        using (var project = connection.CreateCommand())
        {
            project.Transaction = transaction;
            project.CommandText = "SELECT COALESCE((SELECT project FROM project_repositories WHERE source_project_id=$id), (SELECT MIN(project) FROM task_nodes WHERE source_project_id=$id), (SELECT MIN(project) FROM candidates WHERE source_project_id=$id));";
            project.Parameters.AddWithValue("$id", projectId);
            projectName = project.ExecuteScalar() as string;
        }
        if (projectName is null) { transaction.Rollback(); return null; }
        var now = UtcNow();
        long id;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO candidates(source_report_id, project, title, observation, source_status, source_created_at, received_at, source_url, source_project_id, created_by, source, raw_text) VALUES($reportId,$project,'Новый запрос',$text,'user', $now,$now,'http://127.0.0.1/raw-items',$projectId,$createdBy,'user',$text) RETURNING id;";
            insert.Parameters.AddWithValue("$reportId", $"USER-{Guid.NewGuid():N}");
            insert.Parameters.AddWithValue("$project", projectName);
            insert.Parameters.AddWithValue("$text", rawText);
            insert.Parameters.AddWithValue("$now", now);
            insert.Parameters.AddWithValue("$projectId", projectId);
            insert.Parameters.AddWithValue("$createdBy", author);
            id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        using (var enqueue = connection.CreateCommand())
        {
            enqueue.Transaction = transaction;
            enqueue.CommandText = "INSERT INTO candidate_agent_jobs(candidate_id,status,created_at,updated_at) VALUES($id,'queued',$now,$now);";
            enqueue.Parameters.AddWithValue("$id", id);
            enqueue.Parameters.AddWithValue("$now", now);
            enqueue.ExecuteNonQuery();
        }
        transaction.Commit();
        return Get(id);
    }

    public Candidate? AnswerRawItem(long candidateId, string answer)
    {
        var value = answer?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 10000)
            throw new ArgumentException("Answer must contain 1-10000 characters.", nameof(answer));
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var latest = connection.CreateCommand();
        latest.Transaction = transaction;
        latest.CommandText = "SELECT COALESCE(MAX(round),0) FROM candidate_clarification_rounds WHERE candidate_id=$id;";
        latest.Parameters.AddWithValue("$id", candidateId);
        var round = Convert.ToInt32(latest.ExecuteScalar(), CultureInfo.InvariantCulture);
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE candidate_clarification_rounds SET answer=$answer WHERE candidate_id=$id AND round=$round AND answer IS NULL AND json_array_length(questions_json)>0 AND EXISTS(SELECT 1 FROM candidates WHERE id=$id AND source IN ('user','boyscout')) AND EXISTS(SELECT 1 FROM candidate_agent_jobs WHERE candidate_id=$id AND status='ready');";
        update.Parameters.AddWithValue("$answer", value);
        update.Parameters.AddWithValue("$id", candidateId);
        update.Parameters.AddWithValue("$round", round);
        if (round == 0 || update.ExecuteNonQuery() == 0) { transaction.Rollback(); return null; }
        using var queue = connection.CreateCommand();
        queue.Transaction = transaction;
        queue.CommandText = "UPDATE candidate_agent_jobs SET status='queued', error=NULL, updated_at=$now WHERE candidate_id=$id;";
        queue.Parameters.AddWithValue("$now", UtcNow());
        queue.Parameters.AddWithValue("$id", candidateId);
        queue.ExecuteNonQuery();
        transaction.Commit();
        return Get(candidateId);
    }

    private IReadOnlyList<CandidateClarificationRound> ListCandidateClarificationRounds(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT round, questions_json, answer FROM candidate_clarification_rounds WHERE candidate_id=$id ORDER BY round;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var result = new List<CandidateClarificationRound>();
        while (reader.Read()) result.Add(new(reader.GetInt32(0), JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? [], NullableString(reader, 2)));
        return result;
    }

    public Candidate? SetTriageStatus(long id, string status)
    {
        if (status is not ("triaged" or "dismissed"))
            throw new ArgumentException("Status must be 'triaged' or 'dismissed'.", nameof(status));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE candidates SET triage_status = $status WHERE id = $id;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteNonQuery() == 0 ? null : Get(id);
    }

    public Candidate? AddLink(long id, CandidateLinkRequest request)
    {
        if (request.Relation is not ("related" or "duplicate" or "depends_on" or "blocks"))
            throw new ArgumentException("Unsupported candidate relationship.", nameof(request));
        if (id == request.CandidateId) throw new ArgumentException("A candidate cannot link to itself.", nameof(request));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO candidate_links(candidate_id, related_candidate_id, relation, created_at)
            SELECT $id, $relatedId, $relation, $createdAt
            WHERE EXISTS (SELECT 1 FROM candidates WHERE id = $id)
              AND EXISTS (SELECT 1 FROM candidates WHERE id = $relatedId)
            ON CONFLICT DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$relatedId", request.CandidateId);
        command.Parameters.AddWithValue("$relation", request.Relation);
        command.Parameters.AddWithValue("$createdAt", UtcNow());
        command.ExecuteNonQuery();
        return Get(id);
    }

    public CandidateGraphContext? BuildGraphContext(long candidateId)
    {
        var target = Get(candidateId);
        if (target is null) return null;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, project, observation, impact, suggested_action, localized_title, localized_description FROM candidates WHERE id <> $id AND triage_status <> 'dismissed';";
        command.Parameters.AddWithValue("$id", candidateId);
        using var reader = command.ExecuteReader();
        var candidates = new List<(GraphContextNode Node, string Text)>();
        while (reader.Read())
        {
            candidates.Add((
                new GraphContextNode(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    string.Join("\n", new[] { reader.GetString(3), NullableString(reader, 4), NullableString(reader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    NullableString(reader, 6), NullableString(reader, 7)),
                string.Join(' ', reader.GetString(1), reader.GetString(3), NullableString(reader, 4), NullableString(reader, 5))));
        }
        reader.Close();

        using (var taskCommand = connection.CreateCommand())
        {
            taskCommand.CommandText = "SELECT id, project, title, description, acceptance_criteria, current_consequences, status FROM task_nodes WHERE status = 'todo';";
            using var taskReader = taskCommand.ExecuteReader();
            while (taskReader.Read())
            {
                var taskId = taskReader.GetInt64(0);
                var title = taskReader.GetString(2);
                var description = string.Join("\n", new[] { taskReader.GetString(3), taskReader.GetString(4), NullableString(taskReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value)));
                candidates.Add((new GraphContextNode(-taskId, title, taskReader.GetString(1), description,
                    title, description, "task", taskId, taskReader.GetString(6), taskReader.GetString(4), NullableString(taskReader, 5)),
                    string.Join(' ', title, description)));
            }
        }

        var targetTokens = Tokenize(string.Join(' ', target.Title, target.Observation, target.Impact, target.SuggestedAction));
        var related = candidates
            .Select(candidate => (candidate.Node, Score: Similarity(targetTokens, Tokenize(candidate.Text))))
            .Where(candidate => candidate.Score >= 0.04)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Node.Id)
            .Take(8)
            .Select(candidate => candidate.Node)
            .ToDictionary(node => node.Id);

        var relatedCandidateIds = related.Keys.Where(id => id > 0).ToArray();
        var relatedIdsSql = relatedCandidateIds.Length == 0 ? string.Empty :
            $" OR l.candidate_id IN ({string.Join(',', relatedCandidateIds)}) OR l.related_candidate_id IN ({string.Join(',', relatedCandidateIds)})";
        var relatedCandidateTaskIdsSql = relatedCandidateIds.Length == 0 ? string.Empty :
            $" OR l.candidate_id IN ({string.Join(',', relatedCandidateIds)})";
        using (var neighbors = connection.CreateCommand())
        {
            neighbors.CommandText =
                $"SELECT DISTINCT c.id, c.title, c.project, c.observation, c.impact, c.suggested_action, c.localized_title, c.localized_description FROM candidates c JOIN candidate_links l ON (l.candidate_id = c.id OR l.related_candidate_id = c.id) WHERE c.id <> $target AND (l.candidate_id = $target OR l.related_candidate_id = $target{relatedIdsSql}) ORDER BY c.id LIMIT 16;";
            neighbors.Parameters.AddWithValue("$target", candidateId);
            using var neighborReader = neighbors.ExecuteReader();
            while (neighborReader.Read())
                related.TryAdd(neighborReader.GetInt64(0), new GraphContextNode(neighborReader.GetInt64(0), neighborReader.GetString(1), neighborReader.GetString(2),
                    string.Join("\n", new[] { neighborReader.GetString(3), NullableString(neighborReader, 4), NullableString(neighborReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    NullableString(neighborReader, 6), NullableString(neighborReader, 7)));
        }

        var relatedTaskIds = related.Keys.Where(id => id < 0).Select(id => -id).ToArray();
        var relatedTaskIdsSql = relatedTaskIds.Length == 0 ? string.Empty : $" OR l.task_id IN ({string.Join(',', relatedTaskIds)})";
        using (var taskNeighbors = connection.CreateCommand())
        {
            taskNeighbors.CommandText =
                $"SELECT DISTINCT t.id, t.project, t.title, t.description, t.acceptance_criteria, t.current_consequences, t.status FROM task_nodes t JOIN candidate_task_links l ON l.task_id = t.id WHERE t.status = 'todo' AND (l.candidate_id = $target{relatedCandidateTaskIdsSql}){relatedTaskIdsSql} ORDER BY t.id LIMIT 16;";
            taskNeighbors.Parameters.AddWithValue("$target", candidateId);
            using var taskReader = taskNeighbors.ExecuteReader();
            while (taskReader.Read())
            {
                var taskId = taskReader.GetInt64(0);
                var title = taskReader.GetString(2);
                var description = string.Join("\n", new[] { taskReader.GetString(3), taskReader.GetString(4), NullableString(taskReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value)));
                related.TryAdd(-taskId, new GraphContextNode(-taskId, title, taskReader.GetString(1), description,
                    title, description, "task", taskId, taskReader.GetString(6), taskReader.GetString(4), NullableString(taskReader, 5)));
            }
        }

        var nodeIds = related.Keys.ToArray();
        var edges = new List<GraphContextEdge>();
        var nodeIdsSql = nodeIds.Length == 0 ? string.Empty :
            $" OR candidate_id IN ({string.Join(',', nodeIds)}) OR related_candidate_id IN ({string.Join(',', nodeIds)})";
        using (var edgeCommand = connection.CreateCommand())
        {
            edgeCommand.CommandText =
                $"SELECT candidate_id, related_candidate_id, relation FROM candidate_links WHERE candidate_id = $target OR related_candidate_id = $target{nodeIdsSql} UNION ALL SELECT candidate_id, -task_id, relation FROM candidate_task_links WHERE candidate_id = $target{relatedCandidateTaskIdsSql.Replace("l.", string.Empty, StringComparison.Ordinal)}{relatedTaskIdsSql.Replace("l.", string.Empty, StringComparison.Ordinal)} LIMIT 40;";
            edgeCommand.Parameters.AddWithValue("$target", candidateId);
            using var edgeReader = edgeCommand.ExecuteReader();
            while (edgeReader.Read())
            {
                var fromId = edgeReader.GetInt64(0);
                var toId = edgeReader.GetInt64(1);
                if ((fromId == candidateId || related.ContainsKey(fromId)) && (toId == candidateId || related.ContainsKey(toId)))
                    edges.Add(new GraphContextEdge(fromId, toId, edgeReader.GetString(2)));
            }
        }

        return new CandidateGraphContext(
            candidateId,
            target.SourceReportId,
            target.SourceUrl,
            target.Title,
            string.Join("\n", new[] { target.Observation, target.Impact, target.SuggestedAction }.Where(value => !string.IsNullOrWhiteSpace(value))),
            candidates.Count,
            related.Values.ToArray(),
            edges,
            [],
            target.LocalizedTitle,
            target.LocalizedDescription,
            candidates.Count(candidate => candidate.Node.Kind == "task"),
            target.RawText,
            target.ClarificationRounds,
            target.Source);
    }

    public IReadOnlyList<GraphContextNode> SearchGraph(long candidateId, string query, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 120)
            throw new ArgumentException("Graph search query must contain 1-120 characters.", nameof(query));
        var queryTokens = TokenizeSearch(query);
        if (queryTokens.Count == 0) throw new ArgumentException("Graph search query must include a word of at least two characters.", nameof(query));

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, project, observation, impact, suggested_action, localized_title, localized_description FROM candidates WHERE id <> $id AND triage_status <> 'dismissed';";
        command.Parameters.AddWithValue("$id", candidateId);
        using var reader = command.ExecuteReader();
        var matches = new List<(GraphContextNode Node, double Score)>();
        while (reader.Read())
        {
            var node = new GraphContextNode(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                string.Join("\n", new[] { reader.GetString(3), NullableString(reader, 4), NullableString(reader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value))),
                NullableString(reader, 6), NullableString(reader, 7));
            var searchableText = string.Join(' ', node.Title, node.Project, node.Description);
            var score = Similarity(queryTokens, TokenizeSearch(searchableText));
            if (score > 0) matches.Add((node, score));
        }
        using (var tasksCommand = connection.CreateCommand())
        {
         tasksCommand.CommandText = "SELECT id, title, project, description, acceptance_criteria, current_consequences, status FROM task_nodes WHERE status = 'todo';";
            using var tasksReader = tasksCommand.ExecuteReader();
            while (tasksReader.Read())
            {
                var taskId = tasksReader.GetInt64(0);
                var title = tasksReader.GetString(1);
                var description = string.Join("\n", new[] { tasksReader.GetString(3), tasksReader.GetString(4), NullableString(tasksReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value)));
                var node = new GraphContextNode(-taskId, title, tasksReader.GetString(2), description,
                    title, description, "task", taskId, tasksReader.GetString(6), tasksReader.GetString(4), NullableString(tasksReader, 5));
                var score = Similarity(queryTokens, TokenizeSearch(string.Join(' ', title, node.Project, description)));
                if (score > 0) matches.Add((node, score));
            }
        }
        return matches.OrderByDescending(match => match.Score).ThenBy(match => match.Node.Id)
            .Take(Math.Clamp(limit, 1, 5)).Select(match => match.Node).ToArray();
    }

    public GraphExpansion? ExpandGraphNode(long nodeId)
    {
        using var connection = Open();
        var root = ReadGraphNode(connection, nodeId);
        if (root is null) return null;

        if (nodeId < 0)
        {
            var taskId = -nodeId;
            using var taskNeighbors = connection.CreateCommand();
            taskNeighbors.CommandText =
                "SELECT c.id, c.title, c.project, c.observation, c.impact, c.suggested_action, c.localized_title, c.localized_description FROM candidate_task_links l JOIN candidates c ON c.id = l.candidate_id WHERE l.task_id = $taskId ORDER BY c.id LIMIT 12;";
            taskNeighbors.Parameters.AddWithValue("$taskId", taskId);
            using var taskReader = taskNeighbors.ExecuteReader();
            var taskNodes = new Dictionary<long, GraphContextNode> { [root.Id] = root };
            while (taskReader.Read())
                taskNodes.TryAdd(taskReader.GetInt64(0), new GraphContextNode(taskReader.GetInt64(0), taskReader.GetString(1), taskReader.GetString(2),
                    string.Join("\n", new[] { taskReader.GetString(3), NullableString(taskReader, 4), NullableString(taskReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    NullableString(taskReader, 6), NullableString(taskReader, 7)));
            taskReader.Close();
            using var taskEdgesCommand = connection.CreateCommand();
            taskEdgesCommand.CommandText = "SELECT candidate_id, -task_id, relation FROM candidate_task_links WHERE task_id = $taskId ORDER BY candidate_id LIMIT 24;";
            taskEdgesCommand.Parameters.AddWithValue("$taskId", taskId);
            using var taskEdgesReader = taskEdgesCommand.ExecuteReader();
            var taskEdges = new List<GraphContextEdge>();
            while (taskEdgesReader.Read()) taskEdges.Add(new(taskEdgesReader.GetInt64(0), taskEdgesReader.GetInt64(1), taskEdgesReader.GetString(2)));
            return new GraphExpansion(taskNodes.Values.ToArray(), taskEdges);
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT c.id, c.title, c.project, c.observation, c.impact, c.suggested_action, c.localized_title, c.localized_description
            FROM candidate_links l
            JOIN candidates c ON c.id = CASE WHEN l.candidate_id = $id THEN l.related_candidate_id ELSE l.candidate_id END
            WHERE l.candidate_id = $id OR l.related_candidate_id = $id
            ORDER BY c.id LIMIT 12;
            """;
        command.Parameters.AddWithValue("$id", nodeId);
        using var reader = command.ExecuteReader();
        var nodes = new Dictionary<long, GraphContextNode> { [root.Id] = root };
        while (reader.Read())
        {
            var node = new GraphContextNode(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                string.Join("\n", new[] { reader.GetString(3), NullableString(reader, 4), NullableString(reader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value))),
                NullableString(reader, 6), NullableString(reader, 7));
            nodes.TryAdd(node.Id, node);
        }
        reader.Close();

        using var edgeCommand = connection.CreateCommand();
        edgeCommand.CommandText = "SELECT candidate_id, related_candidate_id, relation FROM candidate_links WHERE candidate_id = $id OR related_candidate_id = $id ORDER BY candidate_id, related_candidate_id LIMIT 24;";
        edgeCommand.Parameters.AddWithValue("$id", nodeId);
        using var edgeReader = edgeCommand.ExecuteReader();
        var edges = new List<GraphContextEdge>();
        while (edgeReader.Read()) edges.Add(new(edgeReader.GetInt64(0), edgeReader.GetInt64(1), edgeReader.GetString(2)));
        using var taskNeighborCommand = connection.CreateCommand();
        taskNeighborCommand.CommandText = "SELECT -t.id, t.title, t.project, t.description, t.acceptance_criteria, t.current_consequences, t.status FROM candidate_task_links l JOIN task_nodes t ON t.id = l.task_id WHERE l.candidate_id = $candidateId ORDER BY t.id LIMIT 12;";
        taskNeighborCommand.Parameters.AddWithValue("$candidateId", nodeId);
        using var taskNeighborReader = taskNeighborCommand.ExecuteReader();
        while (taskNeighborReader.Read())
        {
            var taskId = -taskNeighborReader.GetInt64(0);
            var title = taskNeighborReader.GetString(1);
            var description = string.Join("\n", new[] { taskNeighborReader.GetString(3), taskNeighborReader.GetString(4), NullableString(taskNeighborReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value)));
            nodes.TryAdd(taskId, new GraphContextNode(taskId, title, taskNeighborReader.GetString(2), description,
                title, description, "task", -taskId, taskNeighborReader.GetString(6), taskNeighborReader.GetString(4), NullableString(taskNeighborReader, 5)));
        }
        taskNeighborReader.Close();
        using var candidateTaskEdges = connection.CreateCommand();
        candidateTaskEdges.CommandText = "SELECT candidate_id, -task_id, relation FROM candidate_task_links WHERE candidate_id = $candidateId ORDER BY task_id LIMIT 24;";
        candidateTaskEdges.Parameters.AddWithValue("$candidateId", nodeId);
        using var candidateTaskEdgeReader = candidateTaskEdges.ExecuteReader();
        while (candidateTaskEdgeReader.Read()) edges.Add(new(candidateTaskEdgeReader.GetInt64(0), candidateTaskEdgeReader.GetInt64(1), candidateTaskEdgeReader.GetString(2)));
        return new GraphExpansion(nodes.Values.ToArray(), edges);
    }

    public CandidateGraphContext MergeGraphContext(
        CandidateGraphContext current,
        GraphExpansion expansion,
        GraphExplorationStep step)
    {
        var nodes = current.Nodes.ToDictionary(node => node.Id);
        foreach (var node in expansion.Nodes)
            if (nodes.Count < 40 || nodes.ContainsKey(node.Id)) nodes[node.Id] = node;
        var edges = current.Edges.Concat(expansion.Edges)
            .DistinctBy(edge => (edge.FromId, edge.ToId, edge.Relation)).Take(80).ToArray();
        return current with
        {
            Nodes = nodes.Values.OrderBy(node => node.Id).ToArray(),
            Edges = edges,
            Exploration = current.Exploration.Append(step).Take(6).ToArray(),
        };
    }

    public CandidateGraphContext MergeGraphSearch(
        CandidateGraphContext current,
        IReadOnlyList<GraphContextNode> matches,
        string query)
    {
        var expansion = new GraphExpansion(matches, []);
        return MergeGraphContext(current, expansion,
            new GraphExplorationStep("search", query, matches.Select(node => node.Id).ToArray()));
    }

    public CandidateAgentReview SaveAgentReview(
        CandidateGraphContext context,
        string summary,
        IReadOnlyList<AgentLinkProposal> proposals,
        AgentTaskPlan? taskPlan,
        IReadOnlyList<string>? questions = null)
    {
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("Agent summary is required.", nameof(summary));
        questions ??= [];
        if (questions.Count > 20 || questions.Any(question => !IsRussianText(question) || question.Length > 1000))
            throw new ArgumentException("Clarifying questions must be Russian and contain at most 1000 characters each.", nameof(questions));
        var isRawQueueItem = IsRawQueueSource(Get(context.CandidateId)?.Source);
        if (!isRawQueueItem && (!IsRussianText(context.LocalizedCandidateTitle) || !IsRussianText(context.LocalizedCandidateDescription)))
            throw new ArgumentException("Localized candidate title and description must be Russian.", nameof(context));
        if (taskPlan is not null && (!IsRussianText(taskPlan.Title) || !IsRussianText(taskPlan.Description) || !IsRussianText(taskPlan.AcceptanceCriteria) || !IsRussianText(taskPlan.Rationale)))
            throw new ArgumentException("Task plan fields must be written in Russian.", nameof(taskPlan));
        if (isRawQueueItem && (questions.Count > 0) == (taskPlan is not null))
            throw new ArgumentException("Raw-item analysis must return either clarifying questions without a plan, or a final plan without questions.", nameof(taskPlan));
        if (isRawQueueItem && taskPlan is not null && !HasEstablishedAcceptanceCriteria(taskPlan.AcceptanceCriteria))
            throw new ArgumentException("A final raw-item plan requires established acceptance criteria.", nameof(taskPlan));
        if (isRawQueueItem && taskPlan is not null &&
            (!IsRussianText(context.LocalizedCandidateTitle) || !IsRussianText(context.LocalizedCandidateDescription)))
            throw new ArgumentException("A final raw-item plan requires a generated Russian title and description.", nameof(context));
        var validCandidateTargets = context.Nodes.Where(node => node.Kind == "candidate").Select(node => node.Id).ToHashSet();
        if (proposals.Count > 20 || proposals.Any(proposal =>
                !validCandidateTargets.Contains(proposal.CandidateId) ||
                proposal.Relation is not ("related" or "duplicate" or "depends_on" or "blocks") ||
                string.IsNullOrWhiteSpace(proposal.Rationale) || proposal.Rationale.Length > 1000))
            throw new ArgumentException("The agent proposed an invalid graph link.", nameof(proposals));
        if (taskPlan is not null && (taskPlan.Action is not ("create_task" or "attach_existing" or "update_existing") ||
            (taskPlan.Action == "create_task" && taskPlan.TargetTaskId is not null) ||
            (taskPlan.Action != "create_task" && (taskPlan.TargetTaskId is null ||
                !context.Nodes.Any(node => node.Kind == "task" && node.TaskId == taskPlan.TargetTaskId)))))
            throw new ArgumentException("The agent task plan refers to an invalid action or task node.", nameof(taskPlan));

        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        var createdAt = UtcNow();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO candidate_agent_reviews(candidate_id, summary, context_json, created_at) VALUES($candidateId, $summary, $context, $createdAt) RETURNING id;";
        command.Parameters.AddWithValue("$candidateId", context.CandidateId);
        command.Parameters.AddWithValue("$summary", summary.Trim());
        command.Parameters.AddWithValue("$context", JsonSerializer.Serialize(context));
        command.Parameters.AddWithValue("$createdAt", createdAt);
        var reviewId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

        foreach (var proposal in proposals.DistinctBy(proposal => (proposal.CandidateId, proposal.Relation)))
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO candidate_agent_suggestions(review_id, candidate_id, relation, rationale) VALUES($reviewId, $candidateId, $relation, $rationale);";
            insert.Parameters.AddWithValue("$reviewId", reviewId);
            insert.Parameters.AddWithValue("$candidateId", proposal.CandidateId);
            insert.Parameters.AddWithValue("$relation", proposal.Relation);
            insert.Parameters.AddWithValue("$rationale", proposal.Rationale.Trim());
            insert.ExecuteNonQuery();
        }
        if (taskPlan is not null) using (var taskPlanCommand = connection.CreateCommand())
        {
            taskPlanCommand.Transaction = transaction;
            taskPlanCommand.CommandText = "INSERT INTO candidate_agent_task_plans(review_id, candidate_id, action, target_task_id, title, description, acceptance_criteria, rationale, created_at) VALUES($reviewId, $candidateId, $action, $targetTaskId, $title, $description, $acceptanceCriteria, $rationale, $createdAt);";
            taskPlanCommand.Parameters.AddWithValue("$reviewId", reviewId);
            taskPlanCommand.Parameters.AddWithValue("$candidateId", context.CandidateId);
            taskPlanCommand.Parameters.AddWithValue("$action", taskPlan.Action);
            taskPlanCommand.Parameters.AddWithValue("$targetTaskId", taskPlan.TargetTaskId is null ? DBNull.Value : taskPlan.TargetTaskId.Value);
            taskPlanCommand.Parameters.AddWithValue("$title", taskPlan.Title.Trim());
            taskPlanCommand.Parameters.AddWithValue("$description", taskPlan.Description.Trim());
            taskPlanCommand.Parameters.AddWithValue("$acceptanceCriteria", taskPlan.AcceptanceCriteria.Trim());
            taskPlanCommand.Parameters.AddWithValue("$rationale", taskPlan.Rationale.Trim());
            taskPlanCommand.Parameters.AddWithValue("$createdAt", createdAt);
            taskPlanCommand.ExecuteNonQuery();
        }
        if (isRawQueueItem && (questions.Count > 0 || taskPlan is not null))
        {
            using var round = connection.CreateCommand();
            round.Transaction = transaction;
            round.CommandText = "INSERT INTO candidate_clarification_rounds(candidate_id,round,questions_json,created_at) VALUES($candidateId,(SELECT COALESCE(MAX(round),0)+1 FROM candidate_clarification_rounds WHERE candidate_id=$candidateId),$questions,$createdAt);";
            round.Parameters.AddWithValue("$candidateId", context.CandidateId);
            round.Parameters.AddWithValue("$questions", JsonSerializer.Serialize(questions));
            round.Parameters.AddWithValue("$createdAt", createdAt);
            round.ExecuteNonQuery();
        }
        if (!isRawQueueItem || taskPlan is not null)
        using (var localization = connection.CreateCommand())
        {
            localization.Transaction = transaction;
            localization.CommandText = "UPDATE candidates SET localized_title = $title, localized_description = $description WHERE id = $candidateId;";
            localization.Parameters.AddWithValue("$title", context.LocalizedCandidateTitle);
            localization.Parameters.AddWithValue("$description", context.LocalizedCandidateDescription);
            localization.Parameters.AddWithValue("$candidateId", context.CandidateId);
            localization.ExecuteNonQuery();
        }
        if (isRawQueueItem && taskPlan is { Action: "create_task" } && questions.Count == 0)
        {
            using var createTask = connection.CreateCommand();
            createTask.Transaction = transaction;
            createTask.CommandText = "INSERT INTO task_nodes(source_project_id,source_candidate_id,project,title,description,acceptance_criteria,status,source_report_ids_json,created_by,user_modified,created_at,updated_at) SELECT source_project_id,id,project,$title,$description,$criteria,'todo',json_array(source_report_id),created_by,0,$now,$now FROM candidates WHERE id=$candidateId AND source IN ('user','boyscout') AND length(trim($criteria))>0 AND lower(trim($criteria)) NOT IN ('критерии уточняются.','tbd','todo','n/a') ON CONFLICT(source_candidate_id) DO NOTHING;";
            createTask.Parameters.AddWithValue("$title", taskPlan.Title.Trim());
            createTask.Parameters.AddWithValue("$description", taskPlan.Description.Trim());
            createTask.Parameters.AddWithValue("$criteria", taskPlan.AcceptanceCriteria.Trim());
            createTask.Parameters.AddWithValue("$now", createdAt);
            createTask.Parameters.AddWithValue("$candidateId", context.CandidateId);
            createTask.ExecuteNonQuery();
            using var sourceLink = connection.CreateCommand();
            sourceLink.Transaction = transaction;
            sourceLink.CommandText = "INSERT INTO candidate_task_links(candidate_id,task_id,relation,created_at) SELECT $candidateId,id,'source',$now FROM task_nodes WHERE source_candidate_id=$candidateId ON CONFLICT DO NOTHING; UPDATE candidates SET triage_status='triaged',localized_title=$title,localized_description=$description WHERE id=$candidateId;";
            sourceLink.Parameters.AddWithValue("$candidateId", context.CandidateId);
            sourceLink.Parameters.AddWithValue("$now", createdAt);
            sourceLink.Parameters.AddWithValue("$title", taskPlan.Title.Trim());
            sourceLink.Parameters.AddWithValue("$description", taskPlan.Description.Trim());
            sourceLink.ExecuteNonQuery();
            using var acceptPlan = connection.CreateCommand();
            acceptPlan.Transaction = transaction;
            acceptPlan.CommandText = "UPDATE candidate_agent_task_plans SET status='accepted',target_task_id=(SELECT id FROM task_nodes WHERE source_candidate_id=$candidateId) WHERE review_id=$reviewId;";
            acceptPlan.Parameters.AddWithValue("$candidateId", context.CandidateId);
            acceptPlan.Parameters.AddWithValue("$reviewId", reviewId);
            acceptPlan.ExecuteNonQuery();
        }
        transaction.Commit();
        return GetAgentReview(reviewId)!;
    }

    public CandidateAgentReview? GetLatestAgentReview(long candidateId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM candidate_agent_reviews WHERE candidate_id = $candidateId ORDER BY id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$candidateId", candidateId);
        var result = command.ExecuteScalar();
        return result is null ? null : GetAgentReview(Convert.ToInt64(result, CultureInfo.InvariantCulture));
    }

    public CandidateAgentReview? DecideAgentSuggestion(long candidateId, long suggestionId, bool accept)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT review_id, candidate_id, relation, rationale FROM candidate_agent_suggestions WHERE id = $id AND status = 'pending';";
        select.Parameters.AddWithValue("$id", suggestionId);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
        {
            transaction.Commit();
            return null;
        }
        var reviewId = reader.GetInt64(0);
        var targetId = reader.GetInt64(1);
        var relation = reader.GetString(2);
        reader.Close();

        using var owner = connection.CreateCommand();
        owner.Transaction = transaction;
        owner.CommandText = "SELECT candidate_id FROM candidate_agent_reviews WHERE id = $reviewId;";
        owner.Parameters.AddWithValue("$reviewId", reviewId);
        if (Convert.ToInt64(owner.ExecuteScalar(), CultureInfo.InvariantCulture) != candidateId)
        {
            transaction.Commit();
            return null;
        }

        if (accept)
        {
            using var link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = "INSERT INTO candidate_links(candidate_id, related_candidate_id, relation, created_at) VALUES($candidateId, $targetId, $relation, $createdAt) ON CONFLICT DO NOTHING;";
            link.Parameters.AddWithValue("$candidateId", candidateId);
            link.Parameters.AddWithValue("$targetId", targetId);
            link.Parameters.AddWithValue("$relation", relation);
            link.Parameters.AddWithValue("$createdAt", UtcNow());
            link.ExecuteNonQuery();
        }
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE candidate_agent_suggestions SET status = $status WHERE id = $id;";
            update.Parameters.AddWithValue("$status", accept ? "accepted" : "rejected");
            update.Parameters.AddWithValue("$id", suggestionId);
            update.ExecuteNonQuery();
        }
        transaction.Commit();
        return GetAgentReview(reviewId);
    }

    public CandidateAgentReview? DecideAgentTaskPlan(long candidateId, long planId, bool accept, string? createdBy = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT review_id, action, target_task_id, title, description, acceptance_criteria FROM candidate_agent_task_plans WHERE id = $id AND candidate_id = $candidateId AND status = 'pending';";
        select.Parameters.AddWithValue("$id", planId);
        select.Parameters.AddWithValue("$candidateId", candidateId);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
        {
            transaction.Commit();
            return null;
        }
        var reviewId = reader.GetInt64(0);
        var action = reader.GetString(1);
        var targetTaskId = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2);
        var title = reader.GetString(3);
        var description = reader.GetString(4);
        var acceptanceCriteria = reader.GetString(5);
        reader.Close();

        var taskId = targetTaskId;
        if (accept)
        {
            if (action == "create_task")
            {
                using var candidate = connection.CreateCommand();
                candidate.Transaction = transaction;
                candidate.CommandText = "SELECT project, source_project_id, source_report_id FROM candidates WHERE id = $id;";
                candidate.Parameters.AddWithValue("$id", candidateId);
                using var candidateReader = candidate.ExecuteReader();
                if (!candidateReader.Read())
                {
                    transaction.Rollback();
                    return null;
                }
                var project = candidateReader.GetString(0);
                var projectId = candidateReader.IsDBNull(1) ? (long?)null : candidateReader.GetInt64(1);
                var reportId = candidateReader.GetString(2);
                candidateReader.Close();

                using var insertTask = connection.CreateCommand();
                insertTask.Transaction = transaction;
                insertTask.CommandText = "INSERT INTO task_nodes(source_project_id, source_candidate_id, project, title, description, acceptance_criteria, status, source_report_ids_json, created_by, user_modified, created_at, updated_at) VALUES($projectId, $candidateId, $project, $title, $description, $criteria, 'todo', $sourceReports, $createdBy, 1, $now, $now) RETURNING id;";
                insertTask.Parameters.AddWithValue("$projectId", projectId is null ? DBNull.Value : projectId.Value);
                insertTask.Parameters.AddWithValue("$candidateId", candidateId);
                insertTask.Parameters.AddWithValue("$project", project);
                insertTask.Parameters.AddWithValue("$title", title);
                insertTask.Parameters.AddWithValue("$description", description);
                insertTask.Parameters.AddWithValue("$criteria", acceptanceCriteria);
                insertTask.Parameters.AddWithValue("$sourceReports", JsonSerializer.Serialize(new[] { reportId }));
                insertTask.Parameters.AddWithValue("$createdBy", Db(string.IsNullOrWhiteSpace(createdBy) ? Environment.UserName : createdBy.Trim()));
                insertTask.Parameters.AddWithValue("$now", UtcNow());
                taskId = Convert.ToInt64(insertTask.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            else if (action == "update_existing")
            {
                using var updateTask = connection.CreateCommand();
                updateTask.Transaction = transaction;
                updateTask.CommandText = "UPDATE task_nodes SET title = $title, description = $description, acceptance_criteria = $criteria, user_modified = 1, updated_at = $now WHERE id = $taskId;";
                updateTask.Parameters.AddWithValue("$title", title);
                updateTask.Parameters.AddWithValue("$description", description);
                updateTask.Parameters.AddWithValue("$criteria", acceptanceCriteria);
                updateTask.Parameters.AddWithValue("$now", UtcNow());
                updateTask.Parameters.AddWithValue("$taskId", targetTaskId!.Value);
                if (updateTask.ExecuteNonQuery() == 0)
                {
                    transaction.Rollback();
                    return null;
                }
            }

            if (taskId is not null)
            {
                using var link = connection.CreateCommand();
                link.Transaction = transaction;
                link.CommandText = "INSERT INTO candidate_task_links(candidate_id, task_id, relation, created_at) VALUES($candidateId, $taskId, $relation, $now) ON CONFLICT DO NOTHING;";
                link.Parameters.AddWithValue("$candidateId", candidateId);
                link.Parameters.AddWithValue("$taskId", taskId.Value);
                link.Parameters.AddWithValue("$relation", action == "create_task" ? "source" : "related");
                link.Parameters.AddWithValue("$now", UtcNow());
                link.ExecuteNonQuery();
                using var triage = connection.CreateCommand();
                triage.Transaction = transaction;
                triage.CommandText = "UPDATE candidates SET triage_status = 'triaged' WHERE id = $candidateId;";
                triage.Parameters.AddWithValue("$candidateId", candidateId);
                triage.ExecuteNonQuery();
            }
        }

        using (var updatePlan = connection.CreateCommand())
        {
            updatePlan.Transaction = transaction;
            updatePlan.CommandText = "UPDATE candidate_agent_task_plans SET status = $status, target_task_id = COALESCE($targetTaskId, target_task_id) WHERE id = $id;";
            updatePlan.Parameters.AddWithValue("$status", accept ? "accepted" : "rejected");
            updatePlan.Parameters.AddWithValue("$targetTaskId", taskId is null ? DBNull.Value : taskId.Value);
            updatePlan.Parameters.AddWithValue("$id", planId);
            updatePlan.ExecuteNonQuery();
        }
        transaction.Commit();
        return GetAgentReview(reviewId);
    }

    private CandidateAgentReview? GetAgentReview(long reviewId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, candidate_id, summary, context_json, created_at FROM candidate_agent_reviews WHERE id = $id;";
        command.Parameters.AddWithValue("$id", reviewId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var id = reader.GetInt64(0);
        var candidateId = reader.GetInt64(1);
        var summary = reader.GetString(2);
        var context = JsonSerializer.Deserialize<CandidateGraphContext>(reader.GetString(3))!;
        var createdAt = reader.GetString(4);
        reader.Close();

        using var suggestionsCommand = connection.CreateCommand();
        suggestionsCommand.CommandText = "SELECT s.id, s.candidate_id, COALESCE(c.localized_title, c.title), s.relation, s.rationale, s.status FROM candidate_agent_suggestions s JOIN candidates c ON c.id = s.candidate_id WHERE s.review_id = $reviewId ORDER BY s.id;";
        suggestionsCommand.Parameters.AddWithValue("$reviewId", reviewId);
        using var suggestionsReader = suggestionsCommand.ExecuteReader();
        var suggestions = new List<AgentLinkSuggestion>();
        while (suggestionsReader.Read())
            suggestions.Add(new(suggestionsReader.GetInt64(0), suggestionsReader.GetInt64(1), suggestionsReader.GetString(2), suggestionsReader.GetString(3), suggestionsReader.GetString(4), suggestionsReader.GetString(5)));
        using var planCommand = connection.CreateCommand();
        planCommand.CommandText = "SELECT id, action, target_task_id, title, description, acceptance_criteria, rationale, status FROM candidate_agent_task_plans WHERE review_id = $reviewId;";
        planCommand.Parameters.AddWithValue("$reviewId", reviewId);
        using var planReader = planCommand.ExecuteReader();
        var plan = planReader.Read()
            ? new AgentTaskPlan(planReader.GetString(1), planReader.IsDBNull(2) ? null : planReader.GetInt64(2), planReader.GetString(3), planReader.GetString(4), planReader.GetString(5), planReader.GetString(6), planReader.GetInt64(0), planReader.GetString(7))
            : null;
        using var questionCommand = connection.CreateCommand();
        questionCommand.CommandText = "SELECT questions_json FROM candidate_clarification_rounds WHERE candidate_id=$candidateId ORDER BY round DESC LIMIT 1;";
        questionCommand.Parameters.AddWithValue("$candidateId", candidateId);
        var questions = questionCommand.ExecuteScalar() is string json ? JsonSerializer.Deserialize<string[]>(json) ?? [] : [];
        return new CandidateAgentReview(id, candidateId, summary, context, suggestions, createdAt, plan, questions);
    }

    private static IReadOnlyList<CandidateLink> GetLinks(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT related_candidate_id, relation, COALESCE(localized_title, title)
            FROM candidate_links JOIN candidates ON id = related_candidate_id WHERE candidate_id = $id
            UNION ALL
            SELECT -task_nodes.id, candidate_task_links.relation, task_nodes.title
            FROM candidate_task_links JOIN task_nodes ON task_nodes.id = candidate_task_links.task_id
            WHERE candidate_task_links.candidate_id = $id
            ORDER BY 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var result = new List<CandidateLink>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static GraphContextNode? ReadGraphNode(SqliteConnection connection, long id)
    {
        if (id < 0)
        {
            using var taskCommand = connection.CreateCommand();
            taskCommand.CommandText = "SELECT id, project, title, description, acceptance_criteria, current_consequences, status FROM task_nodes WHERE id = $id AND status = 'todo';";
            taskCommand.Parameters.AddWithValue("$id", -id);
            using var taskReader = taskCommand.ExecuteReader();
            if (!taskReader.Read()) return null;
            var taskId = taskReader.GetInt64(0);
            var taskTitle = taskReader.GetString(2);
            var taskDescription = string.Join("\n", new[] { taskReader.GetString(3), taskReader.GetString(4), NullableString(taskReader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return new GraphContextNode(-taskId, taskTitle, taskReader.GetString(1), taskDescription,
                taskTitle, taskDescription, "task", taskId, taskReader.GetString(6), taskReader.GetString(4), NullableString(taskReader, 5));
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, project, observation, impact, suggested_action, localized_title, localized_description FROM candidates WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new GraphContextNode(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            string.Join("\n", new[] { reader.GetString(3), NullableString(reader, 4), NullableString(reader, 5) }.Where(value => !string.IsNullOrWhiteSpace(value))),
            NullableString(reader, 6), NullableString(reader, 7));
    }

    private static IReadOnlyList<LinkSuggestion> GetSuggestions(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, observation, impact, suggested_action FROM candidates WHERE id <> $id AND triage_status <> 'dismissed';";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var candidates = new List<(long Id, string Title, string Text)>();
        while (reader.Read())
            candidates.Add((reader.GetInt64(0), reader.GetString(1), string.Join(' ', reader.GetString(1), reader.GetString(2), NullableString(reader, 3), NullableString(reader, 4))));
        reader.Close();

        using var source = connection.CreateCommand();
        source.CommandText = "SELECT title, observation, impact, suggested_action FROM candidates WHERE id = $id;";
        source.Parameters.AddWithValue("$id", id);
        using var sourceReader = source.ExecuteReader();
        if (!sourceReader.Read()) return [];
        var sourceWords = Tokenize(string.Join(' ', sourceReader.GetString(0), sourceReader.GetString(1), NullableString(sourceReader, 2), NullableString(sourceReader, 3)));
        sourceReader.Close();

        return candidates.Select(candidate =>
            {
                var words = Tokenize(candidate.Text);
                var overlap = sourceWords.Intersect(words).Count();
                var score = overlap == 0 ? 0 : (double)overlap / sourceWords.Union(words).Count();
                return new LinkSuggestion(candidate.Id, candidate.Title, "related", Math.Round(score, 3));
            })
            .Where(suggestion => suggestion.Score >= 0.08)
            .OrderByDescending(suggestion => suggestion.Score)
            .ThenBy(suggestion => suggestion.CandidateId)
            .Take(5)
            .ToArray();
    }

    private static HashSet<string> Tokenize(string? text) => Words.Matches(text?.ToLowerInvariant() ?? string.Empty).Select(match => match.Value).ToHashSet();

    private static HashSet<string> TokenizeSearch(string? text) => SearchWords.Matches(text?.ToLowerInvariant() ?? string.Empty).Select(match => match.Value).ToHashSet();

    private static double Similarity(HashSet<string> first, HashSet<string> second)
    {
        var union = first.Union(second).Count();
        return union == 0 ? 0 : (double)first.Intersect(second).Count() / union;
    }

    private static bool IsRussianText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var letters = value.Count(char.IsLetter);
        var cyrillic = value.Count(character => character is >= '\u0400' and <= '\u04FF');
        return cyrillic >= 3 && cyrillic * 2 >= letters;
    }

    private static bool HasEstablishedAcceptanceCriteria(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value!.Trim().Length <= 4000 &&
        !new[] { "критерии уточняются", "критерии установлены", "критерии есть", "уточняется", "tbd", "todo", "n/a", "не определено", "будет уточнено" }
            .Any(marker => value!.Trim().TrimEnd('.', '!', '?').Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsRawQueueSource(string? source) => source is "user" or "boyscout";

    private static void ImportTasks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ImportedTask> tasks,
        long candidateId,
        string sourceReportId)
    {
        if (tasks.Count > 500 || tasks.Any(task => task.SourceProjectId <= 0 || string.IsNullOrWhiteSpace(task.SourceTaskId) ||
            string.IsNullOrWhiteSpace(task.Project) || string.IsNullOrWhiteSpace(task.Title) || string.IsNullOrWhiteSpace(task.Description)))
            throw new ArgumentException("Imported task data is invalid or exceeds the 500-task request limit.", nameof(tasks));

        foreach (var task in tasks)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO task_nodes(
                  source_project_id, source_task_id, project, title, description, acceptance_criteria,
                  current_consequences, status, source_report_ids_json, created_by, created_at, updated_at
                ) VALUES(
                  $projectId, $taskId, $project, $title, $description, $acceptanceCriteria,
                  $consequences, $status, $sourceReports, $createdBy, $createdAt, $updatedAt
                )
                ON CONFLICT(source_project_id, source_task_id) DO UPDATE SET
                  project = excluded.project,
                  title = excluded.title,
                  description = excluded.description,
                  acceptance_criteria = excluded.acceptance_criteria,
                  current_consequences = excluded.current_consequences,
                  status = excluded.status,
                  source_report_ids_json = excluded.source_report_ids_json,
                  created_by = COALESCE(excluded.created_by, task_nodes.created_by),
                  updated_at = excluded.updated_at
                WHERE task_nodes.user_modified = 0;
                """;
            command.Parameters.AddWithValue("$projectId", task.SourceProjectId);
            command.Parameters.AddWithValue("$taskId", task.SourceTaskId);
            command.Parameters.AddWithValue("$project", task.Project);
            command.Parameters.AddWithValue("$title", task.Title);
            command.Parameters.AddWithValue("$description", task.Description);
            command.Parameters.AddWithValue("$acceptanceCriteria", task.AcceptanceCriteria);
            command.Parameters.AddWithValue("$consequences", Db(task.CurrentConsequences));
            command.Parameters.AddWithValue("$status", task.Status);
            command.Parameters.AddWithValue("$sourceReports", JsonSerializer.Serialize(task.SourceReportIds));
            command.Parameters.AddWithValue("$createdBy", Db(task.CreatedBy));
            command.Parameters.AddWithValue("$createdAt", task.CreatedAt);
            command.Parameters.AddWithValue("$updatedAt", task.UpdatedAt);
            command.ExecuteNonQuery();

            if (task.SourceReportIds.Contains(sourceReportId, StringComparer.Ordinal))
            {
                using var taskIdCommand = connection.CreateCommand();
                taskIdCommand.Transaction = transaction;
                taskIdCommand.CommandText = "SELECT id FROM task_nodes WHERE source_project_id = $projectId AND source_task_id = $taskId;";
                taskIdCommand.Parameters.AddWithValue("$projectId", task.SourceProjectId);
                taskIdCommand.Parameters.AddWithValue("$taskId", task.SourceTaskId);
                var taskId = Convert.ToInt64(taskIdCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
                using var link = connection.CreateCommand();
                link.Transaction = transaction;
                link.CommandText = "INSERT INTO candidate_task_links(candidate_id, task_id, relation, created_at) VALUES($candidateId, $taskId, 'source', $createdAt) ON CONFLICT DO NOTHING;";
                link.Parameters.AddWithValue("$candidateId", candidateId);
                link.Parameters.AddWithValue("$taskId", taskId);
                link.Parameters.AddWithValue("$createdAt", UtcNow());
                link.ExecuteNonQuery();
            }
        }
    }

    private static void EnsureCandidateColumn(SqliteConnection connection, string columnName, string definition = "TEXT")
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(candidates);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == columnName) return;
        reader.Close();

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE candidates ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private static bool EnsureTaskColumn(SqliteConnection connection, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(task_nodes);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == columnName) return false;
        reader.Close();

        command.CommandText = columnName switch
        {
            "execution_status" => "ALTER TABLE task_nodes ADD COLUMN execution_status TEXT NOT NULL DEFAULT 'idle';",
            "build_status" => "ALTER TABLE task_nodes ADD COLUMN build_status TEXT NOT NULL DEFAULT 'not_started';",
            "merge_request_status" => "ALTER TABLE task_nodes ADD COLUMN merge_request_status TEXT NOT NULL DEFAULT 'none';",
            "approval_state" => "ALTER TABLE task_nodes ADD COLUMN approval_state TEXT NOT NULL DEFAULT 'unknown';",
            "mergeability" => "ALTER TABLE task_nodes ADD COLUMN mergeability TEXT NOT NULL DEFAULT 'unknown';",
            "approved_count" or "required_approvals" => $"ALTER TABLE task_nodes ADD COLUMN {columnName} INTEGER;",
            _ => $"ALTER TABLE task_nodes ADD COLUMN {columnName} TEXT;"
        };
        command.ExecuteNonQuery();
        return true;
    }

    private static void EnsureTaskPreflightColumn(SqliteConnection connection, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(task_preflights);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == columnName) return;
        reader.Close();
        command.CommandText = $"ALTER TABLE task_preflights ADD COLUMN {columnName} INTEGER NOT NULL DEFAULT 0;";
        command.ExecuteNonQuery();
    }

    private static void EnsureProjectRepositoryColumn(SqliteConnection connection, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(project_repositories);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == columnName) return;
        reader.Close();
        command.CommandText = columnName == "code_host"
            ? $"ALTER TABLE project_repositories ADD COLUMN {columnName} TEXT NOT NULL DEFAULT 'gitlab';"
            : $"ALTER TABLE project_repositories ADD COLUMN {columnName} TEXT;";
        command.ExecuteNonQuery();
    }

    private static bool IsGitRepository(string path)
    {
        if (!Directory.Exists(path)) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git")
            {
                ArgumentList = { "-C", path, "rev-parse", "--show-toplevel" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null || !process.WaitForExit(5000))
            {
                if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
                return false;
            }
            return process.ExitCode == 0 && string.Equals(
                Path.GetFullPath(process.StandardOutput.ReadToEnd().Trim()).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
    }

    private static void EnsureMergeRequestCommentColumn(SqliteConnection connection, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(merge_request_comments);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == columnName) return;
        reader.Close();

        command.CommandText = $"ALTER TABLE merge_request_comments ADD COLUMN {columnName} TEXT;";
        command.ExecuteNonQuery();
    }

    private static void Validate(CandidateImport input)
    {
        if (string.IsNullOrWhiteSpace(input.SourceReportId) || input.SourceReportId.Length > 100) throw new ArgumentException("A valid source report ID is required.");
        if (string.IsNullOrWhiteSpace(input.Project) || string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Observation)) throw new ArgumentException("Project, title, and observation are required.");
        if (input.Status is not ("accepted" or "planned"))
            throw new ArgumentException("Only accepted or planned Boy Scout notes can enter the candidate queue.");
        if (input.CreatedBy?.Length > 300) throw new ArgumentException("Candidate creator name is too long.");
        if (!Uri.TryCreate(input.SourceUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Source URL must be a loopback HTTP URL.");
    }

    private static void AddInput(SqliteCommand command, CandidateImport input)
    {
        command.Parameters.AddWithValue("$reportId", input.SourceReportId);
        command.Parameters.AddWithValue("$project", input.Project);
        command.Parameters.AddWithValue("$title", input.Title);
        command.Parameters.AddWithValue("$observation", input.Observation);
        command.Parameters.AddWithValue("$location", Db(input.Location));
        command.Parameters.AddWithValue("$impact", Db(input.Impact));
        command.Parameters.AddWithValue("$suggestedAction", Db(input.SuggestedAction));
        command.Parameters.AddWithValue("$discoveredDuring", Db(input.DiscoveredDuring));
        command.Parameters.AddWithValue("$status", input.Status);
        command.Parameters.AddWithValue("$createdAt", input.CreatedAt);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is ("http" or "https") &&
        !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsValidBranchName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 300 && value == value.Trim() &&
        value[0] != '-' && !value.Contains("..", StringComparison.Ordinal) &&
        !value.Contains("@{", StringComparison.Ordinal) && !value.EndsWith('/') && !value.EndsWith('.') &&
        !value.Split('/').Any(part => part.Length == 0 || part.StartsWith('.') || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) &&
        !value.Any(character => char.IsControl(character) || character is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\');

    private static object Db(string? value) => value is null ? DBNull.Value : value;
    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}
