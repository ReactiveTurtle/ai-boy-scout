using BoyScout.Infrastructure.Persistence;
using BoyScout.Infrastructure.Persistence.Migration;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BoyScout.Core.Tests;

public sealed class LocalProjectSnapshotReaderTests
{
    [Fact]
    public void Empty_project_has_stable_snapshot_and_does_not_modify_database()
    {
        using var db = new Fixture();
        db.Execute("INSERT INTO projects (id, name, created_at, updated_at) VALUES (7, 'Empty', 'created', 'updated');");
        var before = db.DatabaseState();
        var first = db.Reader.Read(7);
        var second = db.Reader.Read(7);

        Assert.Equal(new LocalProjectRow(7, "Empty", "created", "updated"), first.Project);
        Assert.Equal(new LocalSnapshotCounts(0, 0, 0, 0, 0, 0), first.Counts);
        Assert.Empty(first.Paths);
        Assert.Empty(first.Reports);
        Assert.Empty(first.Proposals);
        Assert.Empty(first.Tasks);
        Assert.Empty(first.ReportTaskLinks);
        Assert.Null(first.AiConsent);
        Assert.Equal(64, first.Sha256.Length);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(before, db.DatabaseState());
    }

    [Fact]
    public void Mixed_graph_preserves_raw_fields_links_and_local_ids_independent_of_row_order()
    {
        using var db = new Fixture();
        db.Execute("""
            INSERT INTO projects (id, name, created_at, updated_at) VALUES (7, 'Main', 'c', 'u'), (8, 'Other', 'c', 'u');
            INSERT INTO project_paths (id, project_id, path, is_primary, created_at) VALUES
              (12, 7, 'main', 1, 'c'), (11, 7, 'alias', 0, 'c'), (13, 8, 'other', 1, 'c');
            INSERT INTO reports (sequence, project, title, location, observation, impact, suggested_action,
              discovered_during, status, created_at, updated_at, project_id, source_name, source_installation_id) VALUES
              (22, 'main', 'Second', NULL, 'obs', NULL, NULL, NULL, 'open', 'c', 'u', 7, NULL, NULL),
              (21, 'main', 'First', 'file:1', 'raw', 'impact', 'fix', 'review', 'resolved', 'c', 'u', 7, 'Author', 'source-id'),
              (23, 'other', 'Foreign', NULL, 'obs', NULL, NULL, NULL, 'open', 'c', 'u', 8, NULL, NULL);
            INSERT INTO task_proposals (id, report_sequence, project_id, state, attempts, created_at, updated_at,
              agent_checked_at, human_approved_at, human_rejected_at, localized_report_title, current_consequences) VALUES
              (32, 22, 7, 'failed', 2, 'c', 'u', 'checked', NULL, 'rejected', 'Localized', NULL),
              (31, 21, 7, 'confirmed', 1, 'c', 'u', 'checked', 'approved', NULL, NULL, 'Current impact');
            INSERT INTO tasks (sequence, project_id, title, description, acceptance_criteria, status,
              created_at, updated_at, confirmer_name, confirmer_installation_id, current_consequences) VALUES
              (42, 7, 'Task 2', 'desc', 'criteria', 'todo', 'c', 'u', NULL, NULL, NULL),
              (41, 7, 'Task 1', 'desc', 'criteria', 'done', 'c', 'u', 'Confirmer', 'confirmer-id', 'Current impact');
            INSERT INTO report_tasks (report_sequence, task_sequence) VALUES (22, 42), (21, 42), (22, 41), (21, 41);
            INSERT INTO project_ai_access (project_id, granted_at) VALUES (7, 'consent-date');
            """);
        var before = db.DatabaseState();
        var first = db.Reader.Read(7);
        Assert.Equal(new LocalSnapshotCounts(2, 2, 2, 2, 4, 1), first.Counts);
        Assert.Equal([11L, 12L], first.Paths.Select(x => x.Id));
        Assert.Equal([21L, 22L], first.Reports.Select(x => x.Sequence));
        Assert.Equal("Author", first.Reports[0].SourceName);
        Assert.Equal("source-id", first.Reports[0].SourceInstallationId);
        Assert.Equal("file:1", first.Reports[0].Location);
        Assert.Equal("review", first.Reports[0].DiscoveredDuring);
        Assert.Equal([31L, 32L], first.Proposals.Select(x => x.Id));
        Assert.Equal("approved", first.Proposals[0].HumanApprovedAt);
        Assert.Equal("checked", first.Proposals[1].AgentCheckedAt);
        Assert.Equal("rejected", first.Proposals[1].HumanRejectedAt);
        Assert.Equal("Localized", first.Proposals[1].LocalizedReportTitle);
        Assert.Equal("Current impact", first.Proposals[0].CurrentConsequences);
        Assert.Null(first.Proposals[1].CurrentConsequences);
        Assert.Equal("Current impact", first.Tasks[0].CurrentConsequences);
        Assert.Null(first.Tasks[1].CurrentConsequences);
        Assert.Equal("confirmer-id", first.Tasks[0].ConfirmerInstallationId);
        Assert.Equal(4, first.ReportTaskLinks.Distinct().Count());
        Assert.Equal(new LocalAiConsentRow(7, "consent-date"), first.AiConsent);
        Assert.Equal(before, db.DatabaseState());

        // Reinsert in a different physical order; logical IDs and values stay the same.
        db.Execute("""
            DELETE FROM report_tasks;
            INSERT INTO report_tasks (report_sequence, task_sequence) VALUES (21, 41), (22, 41), (21, 42), (22, 42);
            """);
        Assert.Equal(first.Sha256, db.Reader.Read(7).Sha256);
        db.Execute("UPDATE reports SET observation = 'changed' WHERE sequence = 21;");
        Assert.NotEqual(first.Sha256, db.Reader.Read(7).Sha256);
    }

    [Fact]
    public void Missing_project_and_unsupported_schema_are_rejected_without_migration()
    {
        using var db = new Fixture();
        Assert.Throws<InvalidOperationException>(() => db.Reader.Read(999));
        db.Execute("PRAGMA user_version = 11;");
        Assert.Throws<InvalidOperationException>(() => db.Reader.Read(1));
        Assert.Equal("11", db.DatabaseState().Split('|')[0]);
        db.Execute("PRAGMA user_version = 12; ALTER TABLE reports DROP COLUMN source_name;");
        Assert.Throws<InvalidOperationException>(() => db.Reader.Read(1));
        db.Execute("ALTER TABLE reports ADD COLUMN source_name TEXT; DELETE FROM schema_migrations WHERE version = 12;");
        Assert.Throws<InvalidOperationException>(() => db.Reader.Read(1));
    }

    [Fact]
    public void Missing_consequences_column_is_rejected()
    {
        using var db = new Fixture();
        db.Execute("ALTER TABLE tasks DROP COLUMN current_consequences;");
        Assert.Throws<InvalidOperationException>(() => db.Reader.Read(1));
    }

    [Theory]
    [InlineData("UPDATE task_proposals SET project_id = 8 WHERE id = 31;")]
    [InlineData("INSERT INTO report_tasks (report_sequence, task_sequence) VALUES (21, 42);")]
    [InlineData("UPDATE task_proposals SET report_sequence = 999 WHERE id = 31;")]
    public void Invalid_associations_are_rejected(string sql)
    {
        using var db = new Fixture();
        db.Execute("""
            INSERT INTO projects (id, name, created_at, updated_at) VALUES (7, 'A', 'c', 'u'), (8, 'B', 'c', 'u');
            INSERT INTO reports (sequence, project, title, observation, status, created_at, updated_at, project_id)
              VALUES (21, 'a', 'R', 'obs', 'open', 'c', 'u', 7);
            INSERT INTO task_proposals (id, report_sequence, project_id, state, created_at, updated_at)
              VALUES (31, 21, 7, 'queued', 'c', 'u');
            INSERT INTO tasks (sequence, project_id, title, description, acceptance_criteria, status, created_at, updated_at)
              VALUES (42, 8, 'T', 'd', 'a', 'todo', 'c', 'u');
            """);
        db.Execute("PRAGMA foreign_keys = OFF; " + sql);
        Assert.Throws<InvalidOperationException>(() => db.Reader.Read(7));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "boy-scout-snapshot-" + Guid.NewGuid().ToString("N"));
        private readonly string path;

        public Fixture()
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "data.db");
            new BoyScoutDatabase(path).Migrate();
            SqliteConnection.ClearAllPools();
        }

        public LocalProjectSnapshotReader Reader => new(path);

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string DatabaseState()
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT user_version FROM pragma_user_version) || '|' ||
                  (SELECT COUNT(*) FROM schema_migrations) || '|' ||
                  (SELECT group_concat(sql, ';') FROM (SELECT sql FROM sqlite_master ORDER BY name)) || '|' ||
                  coalesce((SELECT group_concat(id || ':' || name, ';') FROM (SELECT id, name FROM projects ORDER BY id)), '') || '|' ||
                  coalesce((SELECT group_concat(sequence || ':' || observation, ';') FROM (SELECT sequence, observation FROM reports ORDER BY sequence)), '') || '|' ||
                  (SELECT COUNT(*) FROM task_proposals) || '|' || (SELECT COUNT(*) FROM tasks) || '|' ||
                  (SELECT COUNT(*) FROM report_tasks) || '|' || (SELECT COUNT(*) FROM project_ai_access);
                """;
            return (string)command.ExecuteScalar()!;
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
