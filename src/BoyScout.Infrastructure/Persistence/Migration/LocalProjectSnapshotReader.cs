using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BoyScout.Infrastructure.Persistence.Migration;

public sealed class LocalProjectSnapshotReader(string databasePath)
{
    private static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>
    {
        ["projects"] = "id:INTEGER name:TEXT created_at:TEXT updated_at:TEXT",
        ["project_paths"] = "id:INTEGER project_id:INTEGER path:TEXT is_primary:INTEGER created_at:TEXT",
        ["reports"] = "sequence:INTEGER project:TEXT title:TEXT location:TEXT observation:TEXT impact:TEXT suggested_action:TEXT discovered_during:TEXT status:TEXT created_at:TEXT updated_at:TEXT project_id:INTEGER source_name:TEXT source_installation_id:TEXT",
        ["task_proposals"] = "id:INTEGER report_sequence:INTEGER project_id:INTEGER state:TEXT review_intent:TEXT requested_rejection_reason:TEXT requested_clarification:TEXT relevance:TEXT recommended_action:TEXT final_rejection_reason:TEXT verification_summary:TEXT title:TEXT description:TEXT acceptance_criteria:TEXT localized_report_title:TEXT localized_report_observation:TEXT localized_report_impact:TEXT localized_report_suggested_action:TEXT error:TEXT attempts:INTEGER created_at:TEXT updated_at:TEXT agent_checked_at:TEXT human_approved_at:TEXT human_rejected_at:TEXT current_consequences:TEXT",
        ["tasks"] = "sequence:INTEGER project_id:INTEGER title:TEXT description:TEXT acceptance_criteria:TEXT status:TEXT created_at:TEXT updated_at:TEXT confirmer_name:TEXT confirmer_installation_id:TEXT current_consequences:TEXT",
        ["report_tasks"] = "report_sequence:INTEGER task_sequence:INTEGER",
        ["project_ai_access"] = "project_id:INTEGER granted_at:TEXT",
        ["firebase_note_transfers"] = "local_note_id:TEXT firebase_note_id:TEXT transferred_at:TEXT",
    };

    public LocalProjectSnapshot Read(long projectId)
    {
        // Do not use BoyScoutDatabase.OpenConnection/Migrate: both can modify the source database.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt32(version.ExecuteScalar()) != BoyScoutDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException($"Only Boy Scout schema v{BoyScoutDatabase.CurrentSchemaVersion} can be snapshotted.");
        }

        foreach (var (table, expected) in Columns)
        {
            using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = $"PRAGMA table_info({table});";
            using var reader = schema.ExecuteReader();
            var actual = new List<string>();
            while (reader.Read())
                actual.Add($"{reader.GetString(1)}:{reader.GetString(2)}");
            if (!actual.SequenceEqual(expected.Split(' '), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported Boy Scout schema: {table} columns differ from v{BoyScoutDatabase.CurrentSchemaVersion}.");
        }

        using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 12 AND name = 'task_current_consequences';";
            if (Convert.ToInt32(history.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Unsupported Boy Scout schema: v12 migration record is missing.");
            history.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 13 AND name = 'todo_graph_delivery_status';";
            if (Convert.ToInt32(history.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Unsupported Boy Scout schema: v13 migration record is missing.");
            history.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 14 AND name = 'merge_git_worktrees';";
            if (Convert.ToInt32(history.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Unsupported Boy Scout schema: v14 migration record is missing.");
            history.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 15 AND name = 'todo_graph_primary_status';";
            if (Convert.ToInt32(history.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Unsupported Boy Scout schema: v15 migration record is missing.");
            history.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 16 AND name = 'firebase_note_transfer_markers';";
            if (Convert.ToInt32(history.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Unsupported Boy Scout schema: v16 migration record is missing.");
            history.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 17 AND name = 'remove_legacy_delivery_tracking';";
            if (Convert.ToInt32(history.ExecuteScalar()) != 1)
                throw new InvalidOperationException("Unsupported Boy Scout schema: v17 migration record is missing.");
        }

        var project = ReadRows(connection, transaction,
            "SELECT id, name, created_at, updated_at FROM projects WHERE id = $projectId",
            r => new LocalProjectRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)), projectId).SingleOrDefault()
            ?? throw new InvalidOperationException($"Local project {projectId} does not exist.");

        // Check both directions: a selected report may point at a foreign task, and a
        // selected task may point at a foreign report. SQLite FKs alone do not prevent this.
        using (var integrity = connection.CreateCommand())
        {
            integrity.Transaction = transaction;
            integrity.CommandText =
                """
                SELECT 1 FROM task_proposals p LEFT JOIN reports r ON r.sequence = p.report_sequence
                  WHERE r.sequence IS NULL OR p.project_id <> r.project_id
                UNION ALL
                SELECT 1 FROM report_tasks l LEFT JOIN reports r ON r.sequence = l.report_sequence
                  LEFT JOIN tasks t ON t.sequence = l.task_sequence
                  WHERE r.sequence IS NULL OR t.sequence IS NULL OR r.project_id IS NULL
                     OR r.project_id <> t.project_id
                UNION ALL
                SELECT 1 FROM reports r WHERE r.project_id IS NULL
                UNION ALL
                SELECT 1 FROM pragma_foreign_key_check
                LIMIT 1;
                """;
            if (integrity.ExecuteScalar() is not null)
                throw new InvalidOperationException("Local database contains dangling or cross-project associations.");
        }

        var paths = ReadRows(connection, transaction,
            "SELECT id, project_id, path, is_primary, created_at FROM project_paths WHERE project_id = $projectId ORDER BY id",
            r => new LocalProjectPathRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetString(4)), projectId);
        var reports = ReadRows(connection, transaction,
            "SELECT sequence, project, title, location, observation, impact, suggested_action, discovered_during, status, created_at, updated_at, project_id, source_name, source_installation_id FROM reports WHERE project_id = $projectId ORDER BY sequence",
            r => new LocalReportRow(r.GetInt64(0), r.GetString(1), r.GetString(2), Text(r, 3), r.GetString(4), Text(r, 5), Text(r, 6), Text(r, 7), r.GetString(8), r.GetString(9), r.GetString(10), r.IsDBNull(11) ? null : r.GetInt64(11), Text(r, 12), Text(r, 13)), projectId);
        var proposals = ReadRows(connection, transaction,
            "SELECT id, report_sequence, project_id, state, review_intent, requested_rejection_reason, requested_clarification, relevance, recommended_action, final_rejection_reason, verification_summary, title, description, acceptance_criteria, localized_report_title, localized_report_observation, localized_report_impact, localized_report_suggested_action, error, attempts, created_at, updated_at, agent_checked_at, human_approved_at, human_rejected_at, current_consequences FROM task_proposals WHERE project_id = $projectId ORDER BY id",
            r => new LocalProposalRow(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), Text(r, 5), Text(r, 6), Text(r, 7), Text(r, 8), Text(r, 9), Text(r, 10), Text(r, 11), Text(r, 12), Text(r, 13), Text(r, 14), Text(r, 15), Text(r, 16), Text(r, 17), Text(r, 18), r.GetInt64(19), r.GetString(20), r.GetString(21), Text(r, 22), Text(r, 23), Text(r, 24), Text(r, 25)), projectId);
        var tasks = ReadRows(connection, transaction,
            "SELECT sequence, project_id, title, description, acceptance_criteria, status, created_at, updated_at, confirmer_name, confirmer_installation_id, current_consequences FROM tasks WHERE project_id = $projectId ORDER BY sequence",
            r => new LocalTaskRow(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), Text(r, 8), Text(r, 9), Text(r, 10)), projectId);
        var links = ReadRows(connection, transaction,
            "SELECT l.report_sequence, l.task_sequence FROM report_tasks l JOIN reports r ON r.sequence = l.report_sequence WHERE r.project_id = $projectId ORDER BY l.report_sequence, l.task_sequence",
            r => new LocalReportTaskRow(r.GetInt64(0), r.GetInt64(1)), projectId);
        var consent = ReadRows(connection, transaction,
            "SELECT project_id, granted_at FROM project_ai_access WHERE project_id = $projectId",
            r => new LocalAiConsentRow(r.GetInt64(0), r.GetString(1)), projectId).SingleOrDefault();

        var counts = new LocalSnapshotCounts(paths.Count, reports.Count, proposals.Count, tasks.Count, links.Count, consent is null ? 0 : 1);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { project, paths, reports, proposals, tasks, links, consent });
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return new LocalProjectSnapshot(project, paths, reports, proposals, tasks, links, consent, counts, hash);
    }

    private static string? Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    private static IReadOnlyList<T> ReadRows<T>(SqliteConnection connection, SqliteTransaction transaction,
        string sql, Func<SqliteDataReader, T> map, long projectId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$projectId", projectId);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(map(reader));
        return rows.AsReadOnly();
    }
}
