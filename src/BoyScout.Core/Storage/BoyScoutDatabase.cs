using Microsoft.Data.Sqlite;

namespace BoyScout.Core.Storage;

public sealed class BoyScoutDatabase(string databasePath)
{
    public const int CurrentSchemaVersion = 12;

    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    public void Migrate()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(DatabasePath)
                ?? throw new InvalidOperationException("The database path has no parent directory."));

        using var connection = OpenConnection();
        var currentVersion = ReadUserVersion(connection);

        if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {currentVersion} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (currentVersion == 0)
        {
            if (TableExists(connection, "reports"))
            {
                ValidateReportsTable(connection, includeProjectId: false);
            }

            ApplyVersionOne(connection);
            currentVersion = 1;
        }

        if (currentVersion == 1)
        {
            ValidateVersionOne(connection);
            ApplyVersionTwo(connection);
            currentVersion = 2;
        }

        if (currentVersion == 2)
        {
            ValidateVersionTwo(connection);
            ApplyVersionThree(connection);
            currentVersion = 3;
        }

        if (currentVersion == 3)
        {
            ValidateVersionThree(connection);
            ApplyVersionFour(connection);
            currentVersion = 4;
        }

        if (currentVersion == 4)
        {
            ValidateVersionFour(connection);
            ApplyVersionFive(connection);
            currentVersion = 5;
        }

        if (currentVersion == 5)
        {
            ValidateVersionFive(connection);
            ApplyVersionSix(connection);
            currentVersion = 6;
        }

        if (currentVersion == 6)
        {
            ValidateVersionSix(connection);
            ApplyVersionSeven(connection);
            currentVersion = 7;
        }

        if (currentVersion == 7)
        {
            ValidateVersionSeven(connection);
            ApplyVersionEight(connection);
            currentVersion = 8;
        }

        if (currentVersion == 8)
        {
            ValidateVersionEight(connection);
            ApplyVersionNine(connection);
            currentVersion = 9;
        }

        if (currentVersion == 9)
        {
            ValidateVersionNine(connection);
            ApplyVersionTen(connection);
            currentVersion = 10;
        }

        if (currentVersion == 10)
        {
            ValidateVersionTen(connection);
            ApplyVersionEleven(connection);
            currentVersion = 11;
        }

        if (currentVersion == 11)
        {
            ValidateVersionEleven(connection);
            ApplyVersionTwelve(connection);
            currentVersion = 12;
        }

        if (currentVersion == 12)
        {
            ValidateVersionTwelve(connection);
        }

        EnableWriteAheadLogging(connection);
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();

        return connection;
    }

    private static void ApplyVersionOne(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS reports (
              sequence INTEGER PRIMARY KEY AUTOINCREMENT,
              project TEXT NOT NULL,
              title TEXT NOT NULL,
              location TEXT,
              observation TEXT NOT NULL,
              impact TEXT,
              suggested_action TEXT,
              discovered_during TEXT,
              status TEXT NOT NULL DEFAULT 'open'
                CHECK (status IN ('open', 'accepted', 'planned', 'resolved', 'dismissed')),
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS reports_project_status
              ON reports (project, status, sequence DESC);

            CREATE TABLE schema_migrations (
              version INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              applied_at TEXT NOT NULL
            );
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (1, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "baseline_reports");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 1;");
        transaction.Commit();
    }

    private static void ValidateVersionOne(SqliteConnection connection)
    {
        ValidateReportsTable(connection, includeProjectId: false);

        if (!TableExists(connection, "schema_migrations"))
        {
            throw new InvalidOperationException(
                "Database schema version is 1, but the migration history table is missing.");
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 1 AND name = 'baseline_reports';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException("Database schema version 1 has no baseline migration record.");
        }
    }

    private static void ApplyVersionTwo(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE projects (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              name TEXT NOT NULL,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );

            CREATE TABLE project_paths (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
              path TEXT NOT NULL COLLATE NOCASE UNIQUE,
              is_primary INTEGER NOT NULL DEFAULT 0 CHECK (is_primary IN (0, 1)),
              created_at TEXT NOT NULL
            );

            CREATE INDEX project_paths_project ON project_paths (project_id, id);
            CREATE UNIQUE INDEX project_paths_one_primary
              ON project_paths (project_id) WHERE is_primary = 1;

            ALTER TABLE reports
              ADD COLUMN project_id INTEGER REFERENCES projects(id);

            CREATE INDEX reports_project_id_status
              ON reports (project_id, status, sequence DESC);

            CREATE TRIGGER reports_assign_project_after_legacy_insert
            AFTER INSERT ON reports
            WHEN NEW.project_id IS NULL
            BEGIN
              INSERT INTO projects (name, created_at, updated_at)
              SELECT NEW.project, NEW.created_at, NEW.updated_at
              WHERE NOT EXISTS (
                SELECT 1 FROM project_paths WHERE path = NEW.project
              );

              INSERT INTO project_paths (project_id, path, is_primary, created_at)
              SELECT last_insert_rowid(), NEW.project, 1, NEW.created_at
              WHERE NOT EXISTS (
                SELECT 1 FROM project_paths WHERE path = NEW.project
              );

              UPDATE reports
              SET project_id = (
                SELECT project_id FROM project_paths WHERE path = NEW.project
              )
              WHERE sequence = NEW.sequence;
            END;
            """);

        BackfillProjects(connection, transaction);

        var appliedAt = DateTimeOffset.UtcNow.ToString("O");
        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (2, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "normalize_projects");
            historyCommand.Parameters.AddWithValue("$appliedAt", appliedAt);
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 2;");
        transaction.Commit();
    }

    private static void BackfillProjects(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var legacyPaths = new List<string>();
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT project FROM reports GROUP BY project ORDER BY MIN(sequence);";
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                legacyPaths.Add(reader.GetString(0));
            }
        }

        var projectsByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var legacyPath in legacyPaths)
        {
            var normalizedPath = ProjectPathNormalizer.Normalize(legacyPath);
            if (!projectsByPath.TryGetValue(normalizedPath, out var projectId))
            {
                projectId = InsertProject(connection, transaction, normalizedPath);
                projectsByPath.Add(normalizedPath, projectId);
            }

            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                "UPDATE reports SET project_id = $projectId WHERE project = $legacyPath;";
            updateCommand.Parameters.AddWithValue("$projectId", projectId);
            updateCommand.Parameters.AddWithValue("$legacyPath", legacyPath);
            updateCommand.ExecuteNonQuery();
        }

        using var orphanCommand = connection.CreateCommand();
        orphanCommand.Transaction = transaction;
        orphanCommand.CommandText = "SELECT COUNT(*) FROM reports WHERE project_id IS NULL;";
        if (Convert.ToInt32(orphanCommand.ExecuteScalar()) != 0)
        {
            throw new InvalidOperationException("Project backfill left reports without a stable project reference.");
        }
    }

    private static long InsertProject(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string normalizedPath)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var projectName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = normalizedPath;
        }

        using var projectCommand = connection.CreateCommand();
        projectCommand.Transaction = transaction;
        projectCommand.CommandText =
            """
            INSERT INTO projects (name, created_at, updated_at)
            VALUES ($name, $createdAt, $updatedAt)
            RETURNING id;
            """;
        projectCommand.Parameters.AddWithValue("$name", projectName);
        projectCommand.Parameters.AddWithValue("$createdAt", timestamp);
        projectCommand.Parameters.AddWithValue("$updatedAt", timestamp);
        var projectId = Convert.ToInt64(projectCommand.ExecuteScalar());

        using var pathCommand = connection.CreateCommand();
        pathCommand.Transaction = transaction;
        pathCommand.CommandText =
            """
            INSERT INTO project_paths (project_id, path, is_primary, created_at)
            VALUES ($projectId, $path, 1, $createdAt);
            """;
        pathCommand.Parameters.AddWithValue("$projectId", projectId);
        pathCommand.Parameters.AddWithValue("$path", normalizedPath);
        pathCommand.Parameters.AddWithValue("$createdAt", timestamp);
        pathCommand.ExecuteNonQuery();

        return projectId;
    }

    private static void ValidateVersionTwo(SqliteConnection connection)
    {
        ValidateReportsTable(connection, includeProjectId: true);
        ValidateTableColumns(
            connection,
            "projects",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new("INTEGER", false, true),
                ["name"] = new("TEXT", true, false),
                ["created_at"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });
        ValidateTableColumns(
            connection,
            "project_paths",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new("INTEGER", false, true),
                ["project_id"] = new("INTEGER", true, false),
                ["path"] = new("TEXT", true, false),
                ["is_primary"] = new("INTEGER", true, false),
                ["created_at"] = new("TEXT", true, false),
            });

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations WHERE version = 2 AND name = 'normalize_projects';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException("Database schema version 2 has no project migration record.");
            }
        }

        using (var orphanCommand = connection.CreateCommand())
        {
            orphanCommand.CommandText = "SELECT COUNT(*) FROM reports WHERE project_id IS NULL;";
            if (Convert.ToInt32(orphanCommand.ExecuteScalar()) != 0)
            {
                throw new InvalidOperationException("Database schema version 2 contains reports without a project.");
            }
        }

        using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        using var foreignKeyReader = foreignKeyCommand.ExecuteReader();
        if (foreignKeyReader.Read())
        {
            throw new InvalidOperationException("Database schema version 2 contains invalid project references.");
        }

        if (!TriggerExists(connection, "reports_assign_project_after_legacy_insert"))
        {
            throw new InvalidOperationException(
                "Database schema version 2 has no compatibility trigger for legacy report inserts.");
        }
    }

    private static void ApplyVersionThree(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE task_proposals (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              report_sequence INTEGER NOT NULL UNIQUE REFERENCES reports(sequence) ON DELETE RESTRICT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              state TEXT NOT NULL CHECK (state IN ('processing', 'ready', 'failed', 'confirmed')),
              title TEXT,
              description TEXT,
              acceptance_criteria TEXT,
              error TEXT,
              attempts INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );

            CREATE INDEX task_proposals_state ON task_proposals (state, id);

            CREATE TABLE tasks (
              sequence INTEGER PRIMARY KEY AUTOINCREMENT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              title TEXT NOT NULL,
              description TEXT NOT NULL,
              acceptance_criteria TEXT NOT NULL,
              status TEXT NOT NULL CHECK (status IN ('todo', 'done')),
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );

            CREATE INDEX tasks_project_status ON tasks (project_id, status, sequence DESC);

            CREATE TABLE report_tasks (
              report_sequence INTEGER NOT NULL REFERENCES reports(sequence) ON DELETE RESTRICT,
              task_sequence INTEGER NOT NULL REFERENCES tasks(sequence) ON DELETE RESTRICT,
              PRIMARY KEY (report_sequence, task_sequence)
            );
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (3, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "task_pipeline");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 3;");
        transaction.Commit();
    }

    private static void ValidateVersionThree(SqliteConnection connection)
    {
        ValidateVersionTwo(connection);
        ValidateTableColumns(
            connection,
            "task_proposals",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new("INTEGER", false, true),
                ["report_sequence"] = new("INTEGER", true, false),
                ["project_id"] = new("INTEGER", true, false),
                ["state"] = new("TEXT", true, false),
                ["title"] = new("TEXT", false, false),
                ["description"] = new("TEXT", false, false),
                ["acceptance_criteria"] = new("TEXT", false, false),
                ["error"] = new("TEXT", false, false),
                ["attempts"] = new("INTEGER", true, false),
                ["created_at"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });
        ValidateTableColumns(
            connection,
            "tasks",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["sequence"] = new("INTEGER", false, true),
                ["project_id"] = new("INTEGER", true, false),
                ["title"] = new("TEXT", true, false),
                ["description"] = new("TEXT", true, false),
                ["acceptance_criteria"] = new("TEXT", true, false),
                ["status"] = new("TEXT", true, false),
                ["created_at"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });
        ValidateTableColumns(
            connection,
            "report_tasks",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["report_sequence"] = new("INTEGER", true, true),
                ["task_sequence"] = new("INTEGER", true, true),
            });

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations WHERE version = 3 AND name = 'task_pipeline';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException("Database schema version 3 has no task pipeline migration record.");
            }
        }

        ValidateIndexColumns(connection, "task_proposals_state", ["state", "id"]);
        ValidateIndexColumns(connection, "tasks_project_status", ["project_id", "status", "sequence"]);
        ValidateUniqueIndex(connection, "task_proposals", ["report_sequence"]);
        ValidateForeignKey(connection, "task_proposals", "report_sequence", "reports", "sequence", "RESTRICT");
        ValidateForeignKey(connection, "task_proposals", "project_id", "projects", "id");
        ValidateForeignKey(connection, "tasks", "project_id", "projects", "id");
        ValidateForeignKey(connection, "report_tasks", "report_sequence", "reports", "sequence", "RESTRICT");
        ValidateForeignKey(connection, "report_tasks", "task_sequence", "tasks", "sequence", "RESTRICT");

        using (var valuesCommand = connection.CreateCommand())
        {
            valuesCommand.CommandText =
                """
                SELECT
                  (SELECT COUNT(*) FROM task_proposals
                   WHERE state NOT IN ('processing', 'ready', 'failed', 'confirmed') OR attempts < 0)
                  +
                  (SELECT COUNT(*) FROM tasks WHERE status NOT IN ('todo', 'done'));
                """;
            if (Convert.ToInt32(valuesCommand.ExecuteScalar()) != 0)
            {
                throw new InvalidOperationException("Database schema version 3 contains invalid task pipeline values.");
            }
        }

        using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        using var foreignKeyReader = foreignKeyCommand.ExecuteReader();
        if (foreignKeyReader.Read())
        {
            throw new InvalidOperationException("Database schema version 3 contains invalid references.");
        }
    }

    private static void ApplyVersionFour(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE task_proposals_v4 (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              report_sequence INTEGER NOT NULL UNIQUE REFERENCES reports(sequence) ON DELETE RESTRICT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              state TEXT NOT NULL CHECK (
                state IN ('processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected')
              ),
              relevance TEXT CHECK (relevance IN ('current', 'obsolete', 'unverified')),
              verification_summary TEXT,
              title TEXT,
              description TEXT,
              acceptance_criteria TEXT,
              error TEXT,
              attempts INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );

            INSERT INTO task_proposals_v4 (
              id, report_sequence, project_id, state, relevance, verification_summary,
              title, description, acceptance_criteria, error, attempts, created_at, updated_at
            )
            SELECT
              id, report_sequence, project_id,
              CASE WHEN state = 'confirmed' THEN 'confirmed' ELSE 'processing' END,
              CASE WHEN state = 'confirmed' THEN 'current' ELSE NULL END,
              NULL,
              CASE WHEN state = 'confirmed' THEN title ELSE NULL END,
              CASE WHEN state = 'confirmed' THEN description ELSE NULL END,
              CASE WHEN state = 'confirmed' THEN acceptance_criteria ELSE NULL END,
              CASE WHEN state = 'confirmed' THEN error ELSE NULL END,
              attempts, created_at, updated_at
            FROM task_proposals;

            DROP TABLE task_proposals;
            ALTER TABLE task_proposals_v4 RENAME TO task_proposals;
            CREATE INDEX task_proposals_state ON task_proposals (state, id);

            CREATE TABLE automation_settings (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL CHECK (value IN ('true', 'false')),
              updated_at TEXT NOT NULL
            );

            INSERT INTO automation_settings (key, value, updated_at)
            VALUES (
              'ai_processing_enabled', 'true',
              strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
            );
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (4, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "verify_note_relevance");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 4;");
        transaction.Commit();
    }

    private static void ValidateVersionFour(
        SqliteConnection connection,
        bool includeReviewIntent = false,
        bool includeQueuedState = false,
        bool includeRequestedClarification = false,
        bool includeDecisionFields = false)
    {
        ValidateVersionTwo(connection);
        var proposalColumns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = new("INTEGER", false, true),
            ["report_sequence"] = new("INTEGER", true, false),
            ["project_id"] = new("INTEGER", true, false),
            ["state"] = new("TEXT", true, false),
            ["relevance"] = new("TEXT", false, false),
            ["verification_summary"] = new("TEXT", false, false),
            ["title"] = new("TEXT", false, false),
            ["description"] = new("TEXT", false, false),
            ["acceptance_criteria"] = new("TEXT", false, false),
            ["error"] = new("TEXT", false, false),
            ["attempts"] = new("INTEGER", true, false),
            ["created_at"] = new("TEXT", true, false),
            ["updated_at"] = new("TEXT", true, false),
        };
        if (includeReviewIntent)
        {
            proposalColumns["review_intent"] = new("TEXT", true, false);
            proposalColumns["requested_rejection_reason"] = new("TEXT", false, false);
        }

        if (includeRequestedClarification)
        {
            proposalColumns["requested_clarification"] = new("TEXT", false, false);
        }

        if (includeDecisionFields)
        {
            proposalColumns["recommended_action"] = new("TEXT", false, false);
            proposalColumns["final_rejection_reason"] = new("TEXT", false, false);
            proposalColumns["localized_report_title"] = new("TEXT", false, false);
            proposalColumns["localized_report_observation"] = new("TEXT", false, false);
            proposalColumns["localized_report_impact"] = new("TEXT", false, false);
            proposalColumns["localized_report_suggested_action"] = new("TEXT", false, false);
        }

        ValidateTableColumns(
            connection,
            "task_proposals",
            proposalColumns);
        ValidateTableColumns(
            connection,
            "tasks",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["sequence"] = new("INTEGER", false, true),
                ["project_id"] = new("INTEGER", true, false),
                ["title"] = new("TEXT", true, false),
                ["description"] = new("TEXT", true, false),
                ["acceptance_criteria"] = new("TEXT", true, false),
                ["status"] = new("TEXT", true, false),
                ["created_at"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });
        ValidateTableColumns(
            connection,
            "report_tasks",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["report_sequence"] = new("INTEGER", true, true),
                ["task_sequence"] = new("INTEGER", true, true),
            });
        ValidateTableColumns(
            connection,
            "automation_settings",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = new("TEXT", false, true),
                ["value"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations WHERE version = 4 AND name = 'verify_note_relevance';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException("Database schema version 4 has no relevance migration record.");
            }
        }

        using (var settingCommand = connection.CreateCommand())
        {
            settingCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM automation_settings
                WHERE key = 'ai_processing_enabled' AND value IN ('true', 'false');
                """;
            if (Convert.ToInt32(settingCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException(
                    "Database schema version 4 has no valid AI processing setting.");
            }
        }

        ValidateIndexColumns(connection, "task_proposals_state", ["state", "id"]);
        ValidateIndexColumns(connection, "tasks_project_status", ["project_id", "status", "sequence"]);
        ValidateUniqueIndex(connection, "task_proposals", ["report_sequence"]);
        ValidateForeignKey(connection, "task_proposals", "report_sequence", "reports", "sequence", "RESTRICT");
        ValidateForeignKey(connection, "task_proposals", "project_id", "projects", "id");
        ValidateForeignKey(connection, "tasks", "project_id", "projects", "id");
        ValidateForeignKey(connection, "report_tasks", "report_sequence", "reports", "sequence", "RESTRICT");
        ValidateForeignKey(connection, "report_tasks", "task_sequence", "tasks", "sequence", "RESTRICT");

        using (var valuesCommand = connection.CreateCommand())
        {
            valuesCommand.CommandText =
                $"""
                SELECT
                  (SELECT COUNT(*) FROM task_proposals
                   WHERE state NOT IN (
                     {(includeQueuedState ? "'queued', " : string.Empty)}
                     'processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected'
                   )
                   OR (relevance IS NOT NULL
                       AND relevance NOT IN ('current', 'obsolete', 'unverified'))
                   OR attempts < 0)
                  +
                  (SELECT COUNT(*) FROM tasks WHERE status NOT IN ('todo', 'done'));
                """;
            if (Convert.ToInt32(valuesCommand.ExecuteScalar()) != 0)
            {
                throw new InvalidOperationException("Database schema version 4 contains invalid task pipeline values.");
            }
        }

        using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        using var foreignKeyReader = foreignKeyCommand.ExecuteReader();
        if (foreignKeyReader.Read())
        {
            throw new InvalidOperationException("Database schema version 4 contains invalid references.");
        }
    }

    private static void ApplyVersionFive(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE project_ai_access (
              project_id INTEGER PRIMARY KEY REFERENCES projects(id) ON DELETE CASCADE,
              granted_at TEXT NOT NULL
            );
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (5, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "project_ai_read_consent");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 5;");
        transaction.Commit();
    }

    private static void ValidateVersionFive(
        SqliteConnection connection,
        bool includeReviewIntent = false,
        bool includeQueuedState = false,
        bool includeRequestedClarification = false,
        bool includeDecisionFields = false)
    {
        ValidateVersionFour(
            connection,
            includeReviewIntent,
            includeQueuedState,
            includeRequestedClarification,
            includeDecisionFields);
        ValidateTableColumns(
            connection,
            "project_ai_access",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["project_id"] = new("INTEGER", false, true),
                ["granted_at"] = new("TEXT", true, false),
            });

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations"
                + " WHERE version = 5 AND name = 'project_ai_read_consent';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException("Database schema version 5 has no project AI access migration record.");
            }
        }

        ValidateForeignKey(connection, "project_ai_access", "project_id", "projects", "id", "CASCADE");

        using var foreignKeyCommand = connection.CreateCommand();
        foreignKeyCommand.CommandText = "PRAGMA foreign_key_check;";
        using var foreignKeyReader = foreignKeyCommand.ExecuteReader();
        if (foreignKeyReader.Read())
        {
            throw new InvalidOperationException("Database schema version 5 contains invalid references.");
        }
    }

    private static void ApplyVersionSix(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE task_proposals_v6 (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              report_sequence INTEGER NOT NULL UNIQUE REFERENCES reports(sequence) ON DELETE RESTRICT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              state TEXT NOT NULL CHECK (
                state IN ('processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected')
              ),
              review_intent TEXT NOT NULL DEFAULT 'proposal'
                CHECK (review_intent IN ('proposal', 'rejection')),
              requested_rejection_reason TEXT,
              relevance TEXT CHECK (relevance IN ('current', 'obsolete', 'unverified')),
              verification_summary TEXT,
              title TEXT,
              description TEXT,
              acceptance_criteria TEXT,
              error TEXT,
              attempts INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              CHECK (
                (review_intent = 'proposal' AND requested_rejection_reason IS NULL)
                OR
                (review_intent = 'rejection'
                 AND requested_rejection_reason IS NOT NULL
                 AND length(trim(requested_rejection_reason)) > 0)
              )
            );

            INSERT INTO task_proposals_v6 (
              id, report_sequence, project_id, state, review_intent,
              requested_rejection_reason, relevance, verification_summary,
              title, description, acceptance_criteria, error, attempts, created_at, updated_at
            )
            SELECT
              id, report_sequence, project_id, state, 'proposal', NULL, relevance,
              verification_summary, title, description, acceptance_criteria, error,
              attempts, created_at, updated_at
            FROM task_proposals;

            DROP TABLE task_proposals;
            ALTER TABLE task_proposals_v6 RENAME TO task_proposals;
            CREATE INDEX task_proposals_state ON task_proposals (state, id);
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (6, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "rejection_review_intent");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 6;");
        transaction.Commit();
    }

    private static void ValidateVersionSix(SqliteConnection connection, bool includeQueuedState = false)
    {
        ValidateVersionFive(connection, includeReviewIntent: true, includeQueuedState);

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations"
                + " WHERE version = 6 AND name = 'rejection_review_intent';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException(
                    "Database schema version 6 has no rejection review migration record.");
            }
        }

        using var valuesCommand = connection.CreateCommand();
        valuesCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM task_proposals
            WHERE review_intent NOT IN ('proposal', 'rejection')
               OR (review_intent = 'proposal' AND requested_rejection_reason IS NOT NULL)
               OR (review_intent = 'rejection'
                   AND (requested_rejection_reason IS NULL
                        OR length(trim(requested_rejection_reason)) = 0));
            """;
        if (Convert.ToInt32(valuesCommand.ExecuteScalar()) != 0)
        {
            throw new InvalidOperationException(
                "Database schema version 6 contains invalid proposal review intents.");
        }
    }

    private static void ApplyVersionSeven(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE task_proposals_v7 (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              report_sequence INTEGER NOT NULL UNIQUE REFERENCES reports(sequence) ON DELETE RESTRICT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              state TEXT NOT NULL CHECK (
                state IN ('queued', 'processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected')
              ),
              review_intent TEXT NOT NULL DEFAULT 'proposal'
                CHECK (review_intent IN ('proposal', 'rejection')),
              requested_rejection_reason TEXT,
              relevance TEXT CHECK (relevance IN ('current', 'obsolete', 'unverified')),
              verification_summary TEXT,
              title TEXT,
              description TEXT,
              acceptance_criteria TEXT,
              error TEXT,
              attempts INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              CHECK (
                (review_intent = 'proposal' AND requested_rejection_reason IS NULL)
                OR
                (review_intent = 'rejection'
                 AND requested_rejection_reason IS NOT NULL
                 AND length(trim(requested_rejection_reason)) > 0)
              )
            );

            INSERT INTO task_proposals_v7 (
              id, report_sequence, project_id, state, review_intent,
              requested_rejection_reason, relevance, verification_summary,
              title, description, acceptance_criteria, error, attempts, created_at, updated_at
            )
            SELECT
              id, report_sequence, project_id, state, review_intent,
              requested_rejection_reason, relevance, verification_summary,
              title, description, acceptance_criteria, error, attempts, created_at, updated_at
            FROM task_proposals;

            DROP TABLE task_proposals;
            ALTER TABLE task_proposals_v7 RENAME TO task_proposals;
            CREATE INDEX task_proposals_state ON task_proposals (state, id);
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (7, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "queued_task_proposals");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 7;");
        transaction.Commit();
    }

    private static void ValidateVersionSeven(SqliteConnection connection)
    {
        ValidateVersionSix(connection, includeQueuedState: true);

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations"
                + " WHERE version = 7 AND name = 'queued_task_proposals';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException(
                    "Database schema version 7 has no queued proposal migration record.");
            }
        }

        using var valuesCommand = connection.CreateCommand();
        valuesCommand.CommandText =
            "SELECT COUNT(*) FROM task_proposals WHERE state NOT IN"
            + " ('queued', 'processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected');";
        if (Convert.ToInt32(valuesCommand.ExecuteScalar()) != 0)
        {
            throw new InvalidOperationException("Database schema version 7 contains invalid proposal states.");
        }
    }

    private static void ApplyVersionEight(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE task_proposals_v8 (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              report_sequence INTEGER NOT NULL UNIQUE REFERENCES reports(sequence) ON DELETE RESTRICT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              state TEXT NOT NULL CHECK (
                state IN ('queued', 'processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected')
              ),
              review_intent TEXT NOT NULL DEFAULT 'proposal'
                CHECK (review_intent IN ('proposal', 'rejection')),
              requested_rejection_reason TEXT,
              requested_clarification TEXT,
              relevance TEXT CHECK (relevance IN ('current', 'obsolete', 'unverified')),
              verification_summary TEXT,
              title TEXT,
              description TEXT,
              acceptance_criteria TEXT,
              error TEXT,
              attempts INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              CHECK (
                (review_intent = 'proposal' AND requested_rejection_reason IS NULL)
                OR
                (review_intent = 'rejection'
                 AND requested_rejection_reason IS NOT NULL
                 AND length(trim(requested_rejection_reason)) > 0
                 AND requested_clarification IS NULL)
              ),
              CHECK (
                requested_clarification IS NULL
                OR length(trim(requested_clarification)) > 0
              )
            );

            INSERT INTO task_proposals_v8 (
              id, report_sequence, project_id, state, review_intent,
              requested_rejection_reason, requested_clarification, relevance,
              verification_summary, title, description, acceptance_criteria,
              error, attempts, created_at, updated_at
            )
            SELECT
              id, report_sequence, project_id, state, review_intent,
              requested_rejection_reason, NULL, relevance, verification_summary,
              title, description, acceptance_criteria, error, attempts, created_at, updated_at
            FROM task_proposals;

            DROP TABLE task_proposals;
            ALTER TABLE task_proposals_v8 RENAME TO task_proposals;
            CREATE INDEX task_proposals_state ON task_proposals (state, id);
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (8, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "proposal_clarification");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 8;");
        transaction.Commit();
    }

    private static void ValidateVersionEight(SqliteConnection connection)
    {
        ValidateVersionFive(
            connection,
            includeReviewIntent: true,
            includeQueuedState: true,
            includeRequestedClarification: true);
        ValidateTableColumns(
            connection,
            "task_proposals",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new("INTEGER", false, true),
                ["report_sequence"] = new("INTEGER", true, false),
                ["project_id"] = new("INTEGER", true, false),
                ["state"] = new("TEXT", true, false),
                ["review_intent"] = new("TEXT", true, false),
                ["requested_rejection_reason"] = new("TEXT", false, false),
                ["requested_clarification"] = new("TEXT", false, false),
                ["relevance"] = new("TEXT", false, false),
                ["verification_summary"] = new("TEXT", false, false),
                ["title"] = new("TEXT", false, false),
                ["description"] = new("TEXT", false, false),
                ["acceptance_criteria"] = new("TEXT", false, false),
                ["error"] = new("TEXT", false, false),
                ["attempts"] = new("INTEGER", true, false),
                ["created_at"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });

        using (var migrationCommand = connection.CreateCommand())
        {
            migrationCommand.CommandText =
                "SELECT COUNT(*) FROM schema_migrations"
                + " WHERE version = 8 AND name = 'proposal_clarification';";
            if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException(
                    "Database schema version 8 has no proposal clarification migration record.");
            }
        }

        using var valuesCommand = connection.CreateCommand();
        valuesCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM task_proposals
            WHERE (review_intent = 'proposal' AND requested_rejection_reason IS NOT NULL)
               OR (review_intent = 'rejection'
                   AND (requested_rejection_reason IS NULL
                        OR length(trim(requested_rejection_reason)) = 0
                        OR requested_clarification IS NOT NULL))
               OR (requested_clarification IS NOT NULL
                   AND length(trim(requested_clarification)) = 0);
            """;
        if (Convert.ToInt32(valuesCommand.ExecuteScalar()) != 0)
        {
            throw new InvalidOperationException(
                "Database schema version 8 contains invalid proposal clarification data.");
        }
    }

    private static void ApplyVersionNine(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        Execute(
            connection,
            transaction,
            """
            CREATE TABLE task_proposals_v9 (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              report_sequence INTEGER NOT NULL UNIQUE REFERENCES reports(sequence) ON DELETE RESTRICT,
              project_id INTEGER NOT NULL REFERENCES projects(id),
              state TEXT NOT NULL CHECK (
                state IN ('queued', 'processing', 'ready', 'unverified', 'failed', 'confirmed', 'rejected')
              ),
              review_intent TEXT NOT NULL DEFAULT 'proposal'
                CHECK (review_intent IN ('proposal', 'rejection')),
              requested_rejection_reason TEXT,
              requested_clarification TEXT,
              relevance TEXT CHECK (relevance IN ('current', 'obsolete', 'unverified')),
              recommended_action TEXT CHECK (
                recommended_action IS NULL OR recommended_action IN ('create_task', 'reject')
              ),
              final_rejection_reason TEXT,
              verification_summary TEXT,
              title TEXT,
              description TEXT,
              acceptance_criteria TEXT,
              localized_report_title TEXT,
              localized_report_observation TEXT,
              localized_report_impact TEXT,
              localized_report_suggested_action TEXT,
              error TEXT,
              attempts INTEGER NOT NULL DEFAULT 0,
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL,
              CHECK (
                (review_intent = 'proposal' AND requested_rejection_reason IS NULL)
                OR
                (review_intent = 'rejection'
                 AND requested_rejection_reason IS NOT NULL
                 AND length(trim(requested_rejection_reason)) > 0
                 AND requested_clarification IS NULL)
              ),
              CHECK (requested_clarification IS NULL OR length(trim(requested_clarification)) > 0),
              CHECK (
                state <> 'ready'
                OR (
                  relevance IS NOT NULL
                  AND recommended_action IS NOT NULL
                  AND (
                    (
                      relevance = 'current'
                      AND recommended_action = 'create_task'
                      AND final_rejection_reason IS NULL
                      AND title IS NOT NULL
                      AND description IS NOT NULL
                      AND acceptance_criteria IS NOT NULL
                      AND length(trim(title)) > 0
                      AND length(trim(description)) > 0
                      AND length(trim(acceptance_criteria)) > 0
                    )
                    OR (
                      relevance = 'obsolete'
                      AND recommended_action = 'reject'
                      AND final_rejection_reason IS NOT NULL
                      AND length(trim(final_rejection_reason)) > 0
                      AND title IS NULL
                      AND description IS NULL
                      AND acceptance_criteria IS NULL
                    )
                  )
                )
              ),
              CHECK (
                state <> 'unverified'
                OR (recommended_action IS NULL AND final_rejection_reason IS NULL)
              )
            );

            WITH source AS (
              SELECT *, CASE WHEN
                state = 'ready'
                AND relevance = 'current'
                AND title IS NOT NULL AND length(trim(title)) > 0
                AND description IS NOT NULL AND length(trim(description)) > 0
                AND acceptance_criteria IS NOT NULL AND length(trim(acceptance_criteria)) > 0
                THEN 1 ELSE 0 END AS valid_ready_current
              FROM task_proposals
            )
            INSERT INTO task_proposals_v9 (
              id, report_sequence, project_id, state, review_intent,
              requested_rejection_reason, requested_clarification, relevance,
              recommended_action, final_rejection_reason, verification_summary,
              title, description, acceptance_criteria,
              localized_report_title, localized_report_observation,
              localized_report_impact, localized_report_suggested_action,
              error, attempts, created_at, updated_at
            )
            SELECT
              id, report_sequence, project_id,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN 'queued' ELSE state END,
              review_intent, requested_rejection_reason, requested_clarification,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN NULL ELSE relevance END,
              CASE
                WHEN valid_ready_current THEN 'create_task'
                WHEN state = 'rejected' AND relevance = 'obsolete' THEN 'reject'
                ELSE NULL
              END,
              CASE
                WHEN state = 'rejected' AND relevance = 'obsolete'
                  THEN COALESCE(
                    NULLIF(trim(verification_summary), ''),
                    NULLIF(trim(requested_rejection_reason), '')
                  )
                ELSE NULL
              END,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN NULL ELSE verification_summary END,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN NULL ELSE title END,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN NULL ELSE description END,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN NULL ELSE acceptance_criteria END,
              NULL, NULL, NULL, NULL,
              CASE WHEN state = 'ready' AND NOT valid_ready_current THEN NULL ELSE error END,
              attempts, created_at, updated_at
            FROM source;

            DROP TABLE task_proposals;
            ALTER TABLE task_proposals_v9 RENAME TO task_proposals;
            CREATE INDEX task_proposals_state ON task_proposals (state, id);
            """);

        using (var historyCommand = connection.CreateCommand())
        {
            historyCommand.Transaction = transaction;
            historyCommand.CommandText =
                "INSERT INTO schema_migrations (version, name, applied_at) VALUES (9, $name, $appliedAt);";
            historyCommand.Parameters.AddWithValue("$name", "human_confirmed_agent_decision");
            historyCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
            historyCommand.ExecuteNonQuery();
        }

        Execute(connection, transaction, "PRAGMA user_version = 9;");
        transaction.Commit();
    }

    private static void ValidateVersionNine(SqliteConnection connection)
    {
        ValidateVersionFive(
            connection,
            includeReviewIntent: true,
            includeQueuedState: true,
            includeRequestedClarification: true,
            includeDecisionFields: true);
        ValidateTableColumns(
            connection,
            "task_proposals",
            new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = new("INTEGER", false, true),
                ["report_sequence"] = new("INTEGER", true, false),
                ["project_id"] = new("INTEGER", true, false),
                ["state"] = new("TEXT", true, false),
                ["review_intent"] = new("TEXT", true, false),
                ["requested_rejection_reason"] = new("TEXT", false, false),
                ["requested_clarification"] = new("TEXT", false, false),
                ["relevance"] = new("TEXT", false, false),
                ["recommended_action"] = new("TEXT", false, false),
                ["final_rejection_reason"] = new("TEXT", false, false),
                ["verification_summary"] = new("TEXT", false, false),
                ["title"] = new("TEXT", false, false),
                ["description"] = new("TEXT", false, false),
                ["acceptance_criteria"] = new("TEXT", false, false),
                ["localized_report_title"] = new("TEXT", false, false),
                ["localized_report_observation"] = new("TEXT", false, false),
                ["localized_report_impact"] = new("TEXT", false, false),
                ["localized_report_suggested_action"] = new("TEXT", false, false),
                ["error"] = new("TEXT", false, false),
                ["attempts"] = new("INTEGER", true, false),
                ["created_at"] = new("TEXT", true, false),
                ["updated_at"] = new("TEXT", true, false),
            });

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.CommandText =
            "SELECT COUNT(*) FROM schema_migrations"
            + " WHERE version = 9 AND name = 'human_confirmed_agent_decision';";
        if (Convert.ToInt32(migrationCommand.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException(
                "Database schema version 9 has no human-confirmed decision migration record.");
        }
    }

    private static void ApplyVersionTen(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction,
            """
            ALTER TABLE reports ADD COLUMN source_name TEXT;
            ALTER TABLE reports ADD COLUMN source_installation_id TEXT;
            ALTER TABLE tasks ADD COLUMN confirmer_name TEXT;
            ALTER TABLE tasks ADD COLUMN confirmer_installation_id TEXT;
            """);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at) VALUES (10, $name, $appliedAt);";
        command.Parameters.AddWithValue("$name", "installation_authors");
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        Execute(connection, transaction, "PRAGMA user_version = 10;");
        transaction.Commit();
    }

    private static void ValidateVersionTen(SqliteConnection connection)
    {
        ValidateVersionNine(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 10 AND name = 'installation_authors';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Database schema version 10 has no installation authors migration record.");
        ValidateTableColumns(connection, "reports", new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["source_name"] = new("TEXT", false, false),
            ["source_installation_id"] = new("TEXT", false, false),
        }, additionalOnly: true);
        ValidateTableColumns(connection, "tasks", new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["confirmer_name"] = new("TEXT", false, false),
            ["confirmer_installation_id"] = new("TEXT", false, false),
        }, additionalOnly: true);
    }

    private static void ApplyVersionEleven(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction,
            """
            ALTER TABLE task_proposals ADD COLUMN agent_checked_at TEXT;
            ALTER TABLE task_proposals ADD COLUMN human_approved_at TEXT;
            ALTER TABLE task_proposals ADD COLUMN human_rejected_at TEXT;
            """);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at) VALUES (11, $name, $appliedAt);";
        command.Parameters.AddWithValue("$name", "proposal_decision_timestamps");
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        Execute(connection, transaction, "PRAGMA user_version = 11;");
        transaction.Commit();
    }

    private static void ValidateVersionEleven(SqliteConnection connection)
    {
        ValidateVersionTen(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 11 AND name = 'proposal_decision_timestamps';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Database schema version 11 has no proposal decision timestamps migration record.");
        ValidateTableColumns(connection, "task_proposals", new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["agent_checked_at"] = new("TEXT", false, false),
            ["human_approved_at"] = new("TEXT", false, false),
            ["human_rejected_at"] = new("TEXT", false, false),
        }, additionalOnly: true);
    }

    private static void ApplyVersionTwelve(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction,
            """
            ALTER TABLE task_proposals ADD COLUMN current_consequences TEXT;
            ALTER TABLE tasks ADD COLUMN current_consequences TEXT;
            """);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO schema_migrations (version, name, applied_at) VALUES (12, $name, $appliedAt);";
        command.Parameters.AddWithValue("$name", "task_current_consequences");
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        Execute(connection, transaction, "PRAGMA user_version = 12;");
        transaction.Commit();
    }

    private static void ValidateVersionTwelve(SqliteConnection connection)
    {
        ValidateVersionEleven(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 12 AND name = 'task_current_consequences';";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("Database schema version 12 has no task consequences migration record.");
        foreach (var table in new[] { "task_proposals", "tasks" })
        {
            ValidateTableColumns(connection, table, new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["current_consequences"] = new("TEXT", false, false),
            }, additionalOnly: true);
        }
    }

    private static void ValidateReportsTable(
        SqliteConnection connection,
        bool includeProjectId)
    {
        var expectedColumns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["sequence"] = new("INTEGER", false, true),
            ["project"] = new("TEXT", true, false),
            ["title"] = new("TEXT", true, false),
            ["location"] = new("TEXT", false, false),
            ["observation"] = new("TEXT", true, false),
            ["impact"] = new("TEXT", false, false),
            ["suggested_action"] = new("TEXT", false, false),
            ["discovered_during"] = new("TEXT", false, false),
            ["status"] = new("TEXT", true, false),
            ["created_at"] = new("TEXT", true, false),
            ["updated_at"] = new("TEXT", true, false),
        };

        if (includeProjectId)
        {
            expectedColumns["project_id"] = new("INTEGER", false, false);
        }

        ValidateTableColumns(connection, "reports", expectedColumns);

        using var statusCommand = connection.CreateCommand();
        statusCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM reports
            WHERE status NOT IN ('open', 'accepted', 'planned', 'resolved', 'dismissed');
            """;
        if (Convert.ToInt32(statusCommand.ExecuteScalar()) != 0)
        {
            throw new InvalidOperationException("The existing reports table contains unsupported status values.");
        }
    }

    private static void ValidateTableColumns(
        SqliteConnection connection,
        string tableName,
        IReadOnlyDictionary<string, ColumnDefinition> expectedColumns,
        bool additionalOnly = false)
    {
        if (!TableExists(connection, tableName))
        {
            throw new InvalidOperationException($"The required {tableName} table is missing.");
        }

        var actualColumns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                actualColumns.Add(
                    reader.GetString(1),
                    new ColumnDefinition(
                        reader.GetString(2),
                        reader.GetInt32(3) == 1,
                        reader.GetInt32(5) > 0));
            }
        }

        var version = ReadUserVersion(connection);
        string[] extraAuthors = version >= 10 && !additionalOnly ? tableName switch
        {
            "reports" => new[] { "source_name", "source_installation_id" },
            "tasks" when version >= 12 => new[] { "confirmer_name", "confirmer_installation_id", "current_consequences" },
            "tasks" => new[] { "confirmer_name", "confirmer_installation_id" },
            "task_proposals" when version >= 12 => new[] { "agent_checked_at", "human_approved_at", "human_rejected_at", "current_consequences" },
            "task_proposals" when version >= 11 => new[] { "agent_checked_at", "human_approved_at", "human_rejected_at" },
            _ => [],
        } : [];
        if ((!additionalOnly && actualColumns.Count != expectedColumns.Count + extraAuthors.Length) ||
            expectedColumns.Any(expected =>
                !actualColumns.TryGetValue(expected.Key, out var actual) || actual != expected.Value))
        {
            throw new InvalidOperationException(
                $"The existing {tableName} table does not match the supported Boy Scout schema.");
        }
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void EnableWriteAheadLogging(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        var journalMode = Convert.ToString(command.ExecuteScalar());
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SQLite did not enable WAL mode. Current mode: {journalMode}.");
        }
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static bool TriggerExists(SqliteConnection connection, string triggerName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name = $name;";
        command.Parameters.AddWithValue("$name", triggerName);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void ValidateIndexColumns(
        SqliteConnection connection,
        string indexName,
        IReadOnlyList<string> expectedColumns)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_info({indexName});";
        using var reader = command.ExecuteReader();
        var actualColumns = new List<string>();
        while (reader.Read())
        {
            actualColumns.Add(reader.GetString(2));
        }

        if (!actualColumns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Database schema has an invalid or missing {indexName} index.");
        }
    }

    private static void ValidateUniqueIndex(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> expectedColumns)
    {
        var uniqueIndexNames = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({tableName});";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt32(2) == 1)
                {
                    uniqueIndexNames.Add(reader.GetString(1));
                }
            }
        }

        foreach (var indexName in uniqueIndexNames)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info('{indexName.Replace("'", "''", StringComparison.Ordinal)}');";
            using var reader = command.ExecuteReader();
            var actualColumns = new List<string>();
            while (reader.Read())
            {
                actualColumns.Add(reader.GetString(2));
            }

            if (actualColumns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Database schema has no required unique index on {tableName}.");
    }

    private static void ValidateForeignKey(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string referencedTable,
        string referencedColumn,
        string? onDelete = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(3), columnName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reader.GetString(2), referencedTable, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reader.GetString(4), referencedColumn, StringComparison.OrdinalIgnoreCase) &&
                (onDelete is null ||
                 string.Equals(reader.GetString(6), onDelete, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Database schema has an invalid {tableName}.{columnName} foreign key.");
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private sealed record ColumnDefinition(string Type, bool NotNull, bool PrimaryKey);
}
