using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

namespace TodoGraph;

public sealed class GitLabMcpSettingsStore
{
    private static readonly Regex EnvironmentName = new("^[A-Z_][A-Z0-9_]{0,63}$", RegexOptions.Compiled);
    private static readonly Regex ToolName = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$", RegexOptions.Compiled);
    private readonly string connectionString;
    private readonly IDataProtector protector;

    public GitLabMcpSettingsStore(string databasePath, IDataProtectionProvider protectionProvider)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        protector = protectionProvider.CreateProtector("TodoGraph.GitLabMcpSettings.v1");
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS gitlab_mcp_settings (
              id INTEGER PRIMARY KEY CHECK(id = 1),
              display_name TEXT NOT NULL,
              read_enabled INTEGER NOT NULL,
              read_executable TEXT NOT NULL,
              read_arguments_json TEXT NOT NULL,
              read_token_environment TEXT NOT NULL,
              read_token_protected TEXT,
              read_tools_json TEXT NOT NULL,
              write_enabled INTEGER NOT NULL,
              write_executable TEXT NOT NULL,
              write_arguments_json TEXT NOT NULL,
              write_token_environment TEXT NOT NULL,
              write_token_protected TEXT,
              write_tools_json TEXT NOT NULL,
              github_enabled INTEGER NOT NULL DEFAULT 0,
              github_executable TEXT NOT NULL DEFAULT '',
              github_arguments_json TEXT NOT NULL DEFAULT '[]',
              github_token_environment TEXT NOT NULL DEFAULT 'GITHUB_TOKEN',
              github_token_protected TEXT,
              github_tools_json TEXT NOT NULL DEFAULT '[]',
              updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "github_enabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "github_executable", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "github_arguments_json", "TEXT NOT NULL DEFAULT '[]'");
        EnsureColumn(connection, "github_token_environment", "TEXT NOT NULL DEFAULT 'GITHUB_TOKEN'");
        EnsureColumn(connection, "github_token_protected", "TEXT");
        EnsureColumn(connection, "github_tools_json", "TEXT NOT NULL DEFAULT '[]'");
    }

    public GitLabMcpSettingsView GetView()
    {
        var stored = Load();
        return stored is null
            ? DefaultView()
            : new(stored.DisplayName, ToView(stored.Read), ToView(stored.Write), ToView(stored.GitHubRead));
    }

    public GitLabMcpSettingsUpdate GetRuntimeSettings()
    {
        var stored = Load();
        if (stored is null)
        {
            var empty = new LocalMcpServerUpdate(false, "", [], "GITLAB_TOKEN", null, false, []);
            return new(Environment.UserName, empty, empty, empty with { TokenEnvironmentVariable = "GITHUB_TOKEN" });
        }
        return new(stored.DisplayName, ToRuntime(stored.Read), ToRuntime(stored.Write), ToRuntime(stored.GitHubRead));
    }

    public GitLabMcpSettingsView Save(GitLabMcpSettingsUpdate request)
    {
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? Environment.UserName : request.DisplayName.Trim();
        if (displayName.Length > 100 || displayName.Any(char.IsControl))
            throw new ArgumentException("Имя пользователя должно содержать не более 100 символов.", nameof(request));
        ValidateServer(request.Read, write: false);
        ValidateServer(request.Write, write: true);
        var githubRead = request.GitHubRead ?? new LocalMcpServerUpdate(false, "", [], "GITHUB_TOKEN", null, false, []);
        ValidateServer(githubRead, write: false);
        var existing = Load();
        var readToken = ProtectIncomingToken(request.Read, existing?.Read.TokenProtected);
        var writeToken = ProtectIncomingToken(request.Write, existing?.Write.TokenProtected);
        var githubToken = ProtectIncomingToken(githubRead, existing?.GitHubRead.TokenProtected);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO gitlab_mcp_settings(
              id, display_name, read_enabled, read_executable, read_arguments_json, read_token_environment,
              read_token_protected, read_tools_json, write_enabled, write_executable, write_arguments_json,
              write_token_environment, write_token_protected, write_tools_json,
              github_enabled, github_executable, github_arguments_json, github_token_environment, github_token_protected, github_tools_json, updated_at
            ) VALUES(
              1, $displayName, $readEnabled, $readExecutable, $readArguments, $readTokenEnvironment,
              $readToken, $readTools, $writeEnabled, $writeExecutable, $writeArguments,
              $writeTokenEnvironment, $writeToken, $writeTools,
              $githubEnabled, $githubExecutable, $githubArguments, $githubTokenEnvironment, $githubToken, $githubTools, $updatedAt
            )
            ON CONFLICT(id) DO UPDATE SET
              display_name = excluded.display_name,
              read_enabled = excluded.read_enabled,
              read_executable = excluded.read_executable,
              read_arguments_json = excluded.read_arguments_json,
              read_token_environment = excluded.read_token_environment,
              read_token_protected = excluded.read_token_protected,
              read_tools_json = excluded.read_tools_json,
              write_enabled = excluded.write_enabled,
              write_executable = excluded.write_executable,
              write_arguments_json = excluded.write_arguments_json,
              write_token_environment = excluded.write_token_environment,
              write_token_protected = excluded.write_token_protected,
              write_tools_json = excluded.write_tools_json,
              github_enabled = excluded.github_enabled,
              github_executable = excluded.github_executable,
              github_arguments_json = excluded.github_arguments_json,
              github_token_environment = excluded.github_token_environment,
              github_token_protected = excluded.github_token_protected,
              github_tools_json = excluded.github_tools_json,
              updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$displayName", displayName);
        AddServer(command, "read", request.Read, readToken);
        AddServer(command, "write", request.Write, writeToken);
        AddServer(command, "github", githubRead, githubToken);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return GetView();
    }

    public bool IsConfigured(string role)
    {
        var stored = Load();
        var server = role switch { "gitlab" => stored?.Read, "gitlab-write" => stored?.Write, "github" => stored?.GitHubRead, _ => null };
        return server is { Enabled: true } && !string.IsNullOrWhiteSpace(server.Executable) &&
            !string.IsNullOrWhiteSpace(server.TokenProtected) && server.Tools.Count > 0;
    }

    private StoredSettings? Load()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT display_name, read_enabled, read_executable, read_arguments_json, read_token_environment, read_token_protected, read_tools_json, write_enabled, write_executable, write_arguments_json, write_token_environment, write_token_protected, write_tools_json, github_enabled, github_executable, github_arguments_json, github_token_environment, github_token_protected, github_tools_json FROM gitlab_mcp_settings WHERE id = 1;";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new(
            reader.GetString(0), ReadServer(reader, 1), ReadServer(reader, 7), ReadServer(reader, 13));
    }

    private StoredServer ReadServer(SqliteDataReader reader, int start) => new(
        reader.GetBoolean(start), reader.GetString(start + 1), DeserializeList(reader.GetString(start + 2)),
        reader.GetString(start + 3), reader.IsDBNull(start + 4) ? null : reader.GetString(start + 4),
        DeserializeList(reader.GetString(start + 5)));

    private string? ProtectIncomingToken(LocalMcpServerUpdate request, string? existingProtected)
    {
        if (request.ClearToken) return null;
        if (string.IsNullOrWhiteSpace(request.Token)) return existingProtected;
        if (request.Token.Length > 5000 || request.Token.Any(char.IsControl))
            throw new ArgumentException("MCP token is invalid or too long.");
        return protector.Protect(request.Token);
    }

    private static void ValidateServer(LocalMcpServerUpdate server, bool write)
    {
        if (server.Arguments is null || server.Arguments.Count > 50 || server.Arguments.Any(argument => argument is null || argument.Length > 1000 || argument.Contains('\0')))
            throw new ArgumentException("Each MCP server can have at most 50 arguments of up to 1000 characters.");
        if (string.IsNullOrWhiteSpace(server.TokenEnvironmentVariable) || !EnvironmentName.IsMatch(server.TokenEnvironmentVariable))
            throw new ArgumentException("Use a valid environment variable name for the MCP token.");
        if (server.Tools is null || server.Tools.Count > 50 || server.Tools.Any(tool => !ToolName.IsMatch(tool)))
            throw new ArgumentException("MCP tool allowlist entries must be valid exact tool names.");
        var tools = server.Tools.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tools.Length != server.Tools.Count) throw new ArgumentException("MCP tool names must be unique.");
        if (server.Tools.Any(tool => tool.Contains("push", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Push tools are not allowed; the human must push the branch.");
        if (!write && server.Tools.Any(IsWriteTool))
            throw new ArgumentException("The read-only GitLab MCP allowlist cannot contain write tools.");
        if (write && server.Tools.Any(tool => !IsAllowedWriteTool(tool)))
            throw new ArgumentException("gitlab-write allowlist may contain only MR lookup/create and discussion/note reply tools; branch, commit, push, merge and other write tools are prohibited.");
        if (server.Enabled && (string.IsNullOrWhiteSpace(server.Executable) || server.Executable.Length > 500 ||
                               server.Executable.Any(char.IsControl) || server.Tools.Count == 0))
            throw new ArgumentException("Enabled MCP servers need an executable and at least one allowed tool.");
    }

    private static bool IsWriteTool(string tool)
    {
        var name = tool.Replace('-', '_');
        return new[] { "create", "update", "delete", "add", "edit", "post", "reply" }
            .Any(operation => name.StartsWith(operation + "_", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("_" + operation + "_", StringComparison.OrdinalIgnoreCase) ||
                              name.EndsWith("_" + operation, StringComparison.OrdinalIgnoreCase)) ||
            name.EndsWith("_merge", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("merge_", StringComparison.OrdinalIgnoreCase) &&
            !name.StartsWith("merge_request_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedWriteTool(string tool)
    {
        var name = tool.ToLowerInvariant().Replace('-', '_');
        var first = name.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var mrLookupOrCreate = (name.Contains("merge_request", StringComparison.Ordinal) ||
                                name.Split('_').Contains("mr", StringComparer.Ordinal)) &&
            (first is "get" or "list" or "search" or "find" or "create");
        var reply = (name.Contains("discussion", StringComparison.Ordinal) || name.Contains("note", StringComparison.Ordinal)) &&
            (first is "create" or "add" or "reply" or "post");
        return mrLookupOrCreate || reply;
    }

    private static void AddServer(SqliteCommand command, string prefix, LocalMcpServerUpdate server, string? protectedToken)
    {
        command.Parameters.AddWithValue($"${prefix}Enabled", server.Enabled);
        command.Parameters.AddWithValue($"${prefix}Executable", server.Executable.Trim());
        command.Parameters.AddWithValue($"${prefix}Arguments", JsonSerializer.Serialize(server.Arguments));
        command.Parameters.AddWithValue($"${prefix}TokenEnvironment", server.TokenEnvironmentVariable.Trim());
        command.Parameters.AddWithValue($"${prefix}Token", Db(protectedToken));
        command.Parameters.AddWithValue($"${prefix}Tools", JsonSerializer.Serialize(server.Tools));
    }

    private static LocalMcpServerView ToView(StoredServer server) => new(
        server.Enabled, server.Executable, server.Arguments, server.TokenEnvironmentVariable,
        !string.IsNullOrWhiteSpace(server.TokenProtected), server.Tools);

    private LocalMcpServerUpdate ToRuntime(StoredServer server) => new(
        server.Enabled, server.Executable, server.Arguments, server.TokenEnvironmentVariable,
        string.IsNullOrWhiteSpace(server.TokenProtected) ? null : protector.Unprotect(server.TokenProtected), false, server.Tools);

    private static GitLabMcpSettingsView DefaultView()
    {
        var empty = new LocalMcpServerView(false, "", [], "GITLAB_TOKEN", false, []);
        return new(Environment.UserName, empty, empty, empty with { TokenEnvironmentVariable = "GITHUB_TOKEN" });
    }

    private static void EnsureColumn(SqliteConnection connection, string name, string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(gitlab_mcp_settings);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(1) == name) return;
        reader.Close();
        command.CommandText = $"ALTER TABLE gitlab_mcp_settings ADD COLUMN {name} {definition};";
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> DeserializeList(string value) => JsonSerializer.Deserialize<IReadOnlyList<string>>(value) ?? [];
    private static object Db(string? value) => value is null ? DBNull.Value : value;

    private sealed record StoredSettings(string DisplayName, StoredServer Read, StoredServer Write, StoredServer GitHubRead);
    private sealed record StoredServer(bool Enabled, string Executable, IReadOnlyList<string> Arguments,
        string TokenEnvironmentVariable, string? TokenProtected, IReadOnlyList<string> Tools);
}
