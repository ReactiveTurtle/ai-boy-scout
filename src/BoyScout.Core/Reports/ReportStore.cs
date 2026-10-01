using System.Globalization;
using BoyScout.Core.Storage;
using Microsoft.Data.Sqlite;

namespace BoyScout.Core.Reports;

public sealed class ReportStore(BoyScoutDatabase database)
{
    public ReportPage Browse(
        long? projectId,
        string? status,
        string? search,
        long? before,
        int limit)
    {
        if (status is not null && !ReportStatuses.IsValid(status))
        {
            throw new ArgumentException($"Unsupported report status: {status}.", nameof(status));
        }

        return BrowseCore(projectId, status, noteStatus: null, reviewState: null, search, before, limit);
    }

    public ReportPage BrowseNotes(
        long? projectId,
        string? noteStatus,
        string? reviewState,
        string? search,
        long? before,
        int limit)
    {
        if (!string.IsNullOrWhiteSpace(noteStatus) && !string.IsNullOrWhiteSpace(reviewState))
        {
            throw new ArgumentException("Note status and review state cannot be combined.");
        }

        if (noteStatus is not null && noteStatus is not ("new" or "processed" or "rejected"))
        {
            throw new ArgumentException($"Unsupported note status: {noteStatus}.", nameof(noteStatus));
        }

        if (reviewState is not null &&
            reviewState is not ("queued" or "checking" or "awaiting_decision" or "processed" or "rejected"))
        {
            throw new ArgumentException($"Unsupported review state: {reviewState}.", nameof(reviewState));
        }

        return BrowseCore(projectId, status: null, noteStatus, reviewState, search, before, limit);
    }

    private ReportPage BrowseCore(
        long? projectId,
        string? status,
        string? noteStatus,
        string? reviewState,
        string? search,
        long? before,
        int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        if (projectId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectId), "Project ID must be positive.");
        }

        if (before <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(before), "Cursor must be positive.");
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        using var connection = database.OpenConnection();

        var totalConditions = new List<string>();
        using var totalCommand = connection.CreateCommand();
        AddBrowseFilters(
            totalCommand,
            totalConditions,
            projectId,
            status,
            noteStatus,
            reviewState,
            normalizedSearch);
        totalCommand.CommandText =
            $"SELECT COUNT(*) FROM reports {BuildWhereClause(totalConditions)};";
        var total = Convert.ToInt64(totalCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

        var pageConditions = new List<string>();
        using var pageCommand = connection.CreateCommand();
        AddBrowseFilters(
            pageCommand,
            pageConditions,
            projectId,
            status,
            noteStatus,
            reviewState,
            normalizedSearch);
        if (before is not null)
        {
            pageConditions.Add("sequence < $before");
            pageCommand.Parameters.AddWithValue("$before", before.Value);
        }

        pageCommand.CommandText =
            $"""
            SELECT
              sequence, project,
              COALESCE((SELECT localized_report_title FROM task_proposals
                        WHERE report_sequence = reports.sequence), title),
              location,
              COALESCE((SELECT localized_report_observation FROM task_proposals
                        WHERE report_sequence = reports.sequence), observation),
              COALESCE((SELECT localized_report_impact FROM task_proposals
                        WHERE report_sequence = reports.sequence), impact),
              COALESCE((SELECT localized_report_suggested_action FROM task_proposals
                        WHERE report_sequence = reports.sequence), suggested_action),
              discovered_during, status, created_at, updated_at
            FROM reports
            {BuildWhereClause(pageConditions)}
            ORDER BY sequence DESC
            LIMIT $pageSize;
            """;
        pageCommand.Parameters.AddWithValue("$pageSize", limit + 1);

        var rows = new List<(long Sequence, BoyScoutReport Report)>();
        using (var reader = pageCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((reader.GetInt64(0), MapReport(reader)));
            }
        }

        var hasMore = rows.Count > limit;
        var pageRows = rows.Take(limit).ToArray();
        return new ReportPage(
            pageRows.Select(row => row.Report).ToArray(),
            total,
            hasMore ? pageRows[^1].Sequence : null);
    }

    public IReadOnlyList<ProjectSummary> ListProjects()
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              projects.id,
              projects.name,
              project_paths.path,
              COUNT(reports.sequence),
              SUM(CASE WHEN reports.status = 'open' THEN 1 ELSE 0 END),
              MAX(reports.created_at),
              project_ai_access.project_id IS NOT NULL,
              MAX(reports.sequence)
            FROM projects
            JOIN project_paths
              ON project_paths.project_id = projects.id
             AND project_paths.is_primary = 1
            LEFT JOIN reports ON reports.project_id = projects.id
            LEFT JOIN project_ai_access ON project_ai_access.project_id = projects.id
            GROUP BY projects.id, projects.name, project_paths.path, project_ai_access.project_id
            ORDER BY MAX(reports.sequence) DESC, projects.name COLLATE NOCASE;
            """;

        var projects = new List<ProjectSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            projects.Add(
                new ProjectSummary(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetBoolean(6)));
        }

        return projects;
    }

    public bool? GetAiAccess(long projectId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT project_ai_access.project_id IS NOT NULL
            FROM projects
            LEFT JOIN project_ai_access ON project_ai_access.project_id = projects.id
            WHERE projects.id = $projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        var result = command.ExecuteScalar();
        return result is null ? null : Convert.ToBoolean(result, CultureInfo.InvariantCulture);
    }

    public bool SetAiAccess(long projectId, bool granted)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using (var projectCommand = connection.CreateCommand())
        {
            projectCommand.Transaction = transaction;
            projectCommand.CommandText = "SELECT COUNT(*) FROM projects WHERE id = $projectId;";
            projectCommand.Parameters.AddWithValue("$projectId", projectId);
            if (Convert.ToInt32(projectCommand.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                transaction.Commit();
                return false;
            }
        }

        using (var accessCommand = connection.CreateCommand())
        {
            accessCommand.Transaction = transaction;
            accessCommand.Parameters.AddWithValue("$projectId", projectId);
            if (granted)
            {
                accessCommand.CommandText =
                    """
                    INSERT INTO project_ai_access (project_id, granted_at)
                    VALUES ($projectId, $grantedAt)
                    ON CONFLICT(project_id) DO UPDATE SET granted_at = excluded.granted_at;
                    """;
                accessCommand.Parameters.AddWithValue("$grantedAt", UtcTimestamp());
            }
            else
            {
                accessCommand.CommandText = "DELETE FROM project_ai_access WHERE project_id = $projectId;";
            }

            accessCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public BoyScoutReport Add(CreateReport request)
    {
        ValidateRequired(request.Title, nameof(request.Title));
        ValidateRequired(request.Observation, nameof(request.Observation));
        ValidateOptional(request.Location, nameof(request.Location));
        ValidateOptional(request.Impact, nameof(request.Impact));
        ValidateOptional(request.SuggestedAction, nameof(request.SuggestedAction));
        ValidateOptional(request.DiscoveredDuring, nameof(request.DiscoveredDuring));

        var projectPath = ProjectPathNormalizer.Normalize(request.Project);
        var timestamp = UtcTimestamp();

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        var projectId = GetOrCreateProject(connection, transaction, projectPath, timestamp);

        long sequence;
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO reports (
                  project, project_id, title, location, observation, impact,
                  suggested_action, discovered_during, status, created_at, updated_at
                ) VALUES (
                  $project, $projectId, $title, $location, $observation, $impact,
                  $suggestedAction, $discoveredDuring, 'open', $createdAt, $updatedAt
                )
                RETURNING sequence;
                """;
            command.Parameters.AddWithValue("$project", projectPath);
            command.Parameters.AddWithValue("$projectId", projectId);
            command.Parameters.AddWithValue("$title", request.Title);
            command.Parameters.AddWithValue("$location", DbValue(request.Location));
            command.Parameters.AddWithValue("$observation", request.Observation);
            command.Parameters.AddWithValue("$impact", DbValue(request.Impact));
            command.Parameters.AddWithValue("$suggestedAction", DbValue(request.SuggestedAction));
            command.Parameters.AddWithValue("$discoveredDuring", DbValue(request.DiscoveredDuring));
            command.Parameters.AddWithValue("$createdAt", timestamp);
            command.Parameters.AddWithValue("$updatedAt", timestamp);
            sequence = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        transaction.Commit();
        return GetBySequence(connection, sequence)
            ?? throw new InvalidOperationException("The created report could not be read back.");
    }

    public IReadOnlyList<BoyScoutReport> List(string? project, string? status, int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 100.");
        }

        if (status is not null && !ReportStatuses.IsValid(status))
        {
            throw new ArgumentException($"Unsupported report status: {status}.", nameof(status));
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var conditions = new List<string>();

        if (project is not null)
        {
            conditions.Add(
                "reports.project_id = (SELECT project_id FROM project_paths WHERE path = $projectPath)");
            command.Parameters.AddWithValue("$projectPath", ProjectPathNormalizer.Normalize(project));
        }

        if (status is not null)
        {
            conditions.Add("reports.status = $status");
            command.Parameters.AddWithValue("$status", status);
        }

        var whereClause = conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
        command.CommandText =
            $"""
            SELECT
              sequence, project,
              COALESCE((SELECT localized_report_title FROM task_proposals
                        WHERE report_sequence = reports.sequence), title),
              location,
              COALESCE((SELECT localized_report_observation FROM task_proposals
                        WHERE report_sequence = reports.sequence), observation),
              COALESCE((SELECT localized_report_impact FROM task_proposals
                        WHERE report_sequence = reports.sequence), impact),
              COALESCE((SELECT localized_report_suggested_action FROM task_proposals
                        WHERE report_sequence = reports.sequence), suggested_action),
              discovered_during, status, created_at, updated_at
            FROM reports
            {whereClause}
            ORDER BY sequence DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var reports = new List<BoyScoutReport>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            reports.Add(MapReport(reader));
        }

        return reports;
    }

    public BoyScoutReport? Get(string id)
    {
        var sequence = ParseId(id);
        using var connection = database.OpenConnection();
        return GetBySequence(connection, sequence);
    }

    public BoyScoutReport? UpdateStatus(string id, string status)
    {
        if (!ReportStatuses.IsValid(status))
        {
            throw new ArgumentException($"Unsupported report status: {status}.", nameof(status));
        }

        var sequence = ParseId(id);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE reports SET status = $status, updated_at = $updatedAt WHERE sequence = $sequence;";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$updatedAt",
            UtcTimestamp());
        command.Parameters.AddWithValue("$sequence", sequence);

        return command.ExecuteNonQuery() == 0 ? null : GetBySequence(connection, sequence);
    }

    private static long GetOrCreateProject(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string projectPath,
        string timestamp)
    {
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = "SELECT project_id FROM project_paths WHERE path = $path;";
            selectCommand.Parameters.AddWithValue("$path", projectPath);
            var existing = selectCommand.ExecuteScalar();
            if (existing is not null)
            {
                return Convert.ToInt64(existing, CultureInfo.InvariantCulture);
            }
        }

        var projectName = Path.GetFileName(projectPath);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            projectName = projectPath;
        }

        long projectId;
        using (var projectCommand = connection.CreateCommand())
        {
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
            projectId = Convert.ToInt64(projectCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var pathCommand = connection.CreateCommand();
        pathCommand.Transaction = transaction;
        pathCommand.CommandText =
            """
            INSERT INTO project_paths (project_id, path, is_primary, created_at)
            VALUES ($projectId, $path, 1, $createdAt);
            """;
        pathCommand.Parameters.AddWithValue("$projectId", projectId);
        pathCommand.Parameters.AddWithValue("$path", projectPath);
        pathCommand.Parameters.AddWithValue("$createdAt", timestamp);
        pathCommand.ExecuteNonQuery();

        return projectId;
    }

    private static BoyScoutReport? GetBySequence(SqliteConnection connection, long sequence)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              sequence, project,
              COALESCE((SELECT localized_report_title FROM task_proposals
                        WHERE report_sequence = reports.sequence), title),
              location,
              COALESCE((SELECT localized_report_observation FROM task_proposals
                        WHERE report_sequence = reports.sequence), observation),
              COALESCE((SELECT localized_report_impact FROM task_proposals
                        WHERE report_sequence = reports.sequence), impact),
              COALESCE((SELECT localized_report_suggested_action FROM task_proposals
                        WHERE report_sequence = reports.sequence), suggested_action),
              discovered_during, status, created_at, updated_at
            FROM reports
            WHERE sequence = $sequence;
            """;
        command.Parameters.AddWithValue("$sequence", sequence);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapReport(reader) : null;
    }

    private static BoyScoutReport MapReport(SqliteDataReader reader) =>
        new(
            FormatId(reader.GetInt64(0)),
            reader.GetString(1),
            reader.GetString(2),
            NullableString(reader, 3),
            reader.GetString(4),
            NullableString(reader, 5),
            NullableString(reader, 6),
            NullableString(reader, 7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10));

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void AddBrowseFilters(
        SqliteCommand command,
        ICollection<string> conditions,
        long? projectId,
        string? status,
        string? noteStatus,
        string? reviewState,
        string? search)
    {
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
        else if (noteStatus == "new")
        {
            conditions.Add("status IN ('open', 'accepted', 'planned')");
        }
        else if (noteStatus == "processed")
        {
            conditions.Add("status = 'resolved'");
        }
        else if (noteStatus == "rejected")
        {
            conditions.Add("status = 'dismissed'");
        }

        if (reviewState is not null)
        {
            conditions.Add(
                """
                CASE
                  WHEN reports.status = 'dismissed' THEN 'rejected'
                  WHEN reports.status = 'resolved' THEN 'processed'
                  WHEN (SELECT state FROM task_proposals WHERE report_sequence = reports.sequence) = 'processing'
                    THEN 'checking'
                  WHEN (SELECT state FROM task_proposals WHERE report_sequence = reports.sequence)
                    IN ('ready', 'unverified', 'failed') THEN 'awaiting_decision'
                  WHEN (SELECT state FROM task_proposals WHERE report_sequence = reports.sequence) = 'confirmed'
                    THEN 'processed'
                  WHEN (SELECT state FROM task_proposals WHERE report_sequence = reports.sequence) = 'rejected'
                    THEN 'rejected'
                  ELSE 'queued'
                END = $reviewState
                """);
            command.Parameters.AddWithValue("$reviewState", reviewState);
        }

        if (search is not null)
        {
            conditions.Add(
                """
                (
                  title LIKE $search ESCAPE '\'
                  OR observation LIKE $search ESCAPE '\'
                  OR COALESCE(location, '') LIKE $search ESCAPE '\'
                  OR COALESCE(impact, '') LIKE $search ESCAPE '\'
                  OR COALESCE(suggested_action, '') LIKE $search ESCAPE '\'
                  OR COALESCE((SELECT localized_report_title FROM task_proposals
                               WHERE report_sequence = reports.sequence), '') LIKE $search ESCAPE '\'
                  OR COALESCE((SELECT localized_report_observation FROM task_proposals
                               WHERE report_sequence = reports.sequence), '') LIKE $search ESCAPE '\'
                  OR COALESCE((SELECT localized_report_impact FROM task_proposals
                               WHERE report_sequence = reports.sequence), '') LIKE $search ESCAPE '\'
                  OR COALESCE((SELECT localized_report_suggested_action FROM task_proposals
                               WHERE report_sequence = reports.sequence), '') LIKE $search ESCAPE '\'
                )
                """);
            command.Parameters.AddWithValue("$search", $"%{EscapeLike(search)}%");
        }
    }

    private static string BuildWhereClause(IReadOnlyCollection<string> conditions) =>
        conditions.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", conditions)}";

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;

    private static string FormatId(long sequence) => $"BSR-{sequence:D6}";

    private static string UtcTimestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static long ParseId(string id)
    {
        if (id.Length != 10 ||
            !id.StartsWith("BSR-", StringComparison.Ordinal) ||
            !long.TryParse(id.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
        {
            throw new ArgumentException($"Invalid Boy Scout report ID: {id}", nameof(id));
        }

        return sequence;
    }

    private static void ValidateRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty when provided.", parameterName);
        }
    }
}
