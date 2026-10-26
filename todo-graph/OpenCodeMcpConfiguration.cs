using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TodoGraph;

public sealed class OpenCodeMcpConfiguration(GitLabMcpSettingsStore settings, IWebHostEnvironment environment)
{
    private const string ReadTokenName = "TODO_GRAPH_GITLAB_READ_TOKEN";
    private const string WriteTokenName = "TODO_GRAPH_GITLAB_WRITE_TOKEN";
    private const string GitHubReadTokenName = "TODO_GRAPH_GITHUB_READ_TOKEN";

    public bool IsServerConfigured(string name) => name switch
    {
        "gitlab" => settings.IsConfigured("gitlab"),
        "gitlab-write" => settings.IsConfigured("gitlab-write"),
        "github" => settings.IsConfigured("github"),
        _ => false,
    };

    public OpenCodeMcpLaunch Create(string agentName)
    {
        var basePath = Path.Combine(environment.ContentRootPath, ".opencode", "todo-graph.json");
        var runtime = settings.GetRuntimeSettings();
        var githubRead = runtime.GitHubRead ?? new LocalMcpServerUpdate(false, "", [], "GITHUB_TOKEN", null, false, []);
        if (!runtime.Read.Enabled && !runtime.Write.Enabled && !githubRead.Enabled)
            return new(basePath, new Dictionary<string, string>(), null);

        var root = JsonNode.Parse(File.ReadAllText(basePath))?.AsObject()
            ?? throw new InvalidOperationException("Todo Graph OpenCode profile is invalid.");
        var servers = new JsonObject();
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (agentName == "todo-graph-mr-reader" && runtime.Read.Enabled)
        {
            servers["gitlab"] = BuildServer(runtime.Read, ReadTokenName);
            variables[ReadTokenName] = runtime.Read.Token ?? string.Empty;
            AddAllowedTools(root, "todo-graph-mr-reader", "gitlab", runtime.Read.Tools);
        }
        if (agentName is ("todo-graph-mr-replier" or "todo-graph-mr-creator") && runtime.Write.Enabled)
        {
            servers["gitlab-write"] = BuildServer(runtime.Write, WriteTokenName);
            variables[WriteTokenName] = runtime.Write.Token ?? string.Empty;
            AddAllowedTools(root, "todo-graph-mr-replier", "gitlab-write", runtime.Write.Tools);
            AddAllowedTools(root, "todo-graph-mr-creator", "gitlab-write", runtime.Write.Tools);
        }
        if (agentName == "todo-graph-pr-reader" && githubRead.Enabled)
        {
            servers["github"] = BuildServer(githubRead, GitHubReadTokenName);
            variables[GitHubReadTokenName] = githubRead.Token ?? string.Empty;
            AddAllowedTools(root, "todo-graph-pr-reader", "github", githubRead.Tools);
        }
        root["mcp"] = servers;

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "todo-graph", "opencode-configs");
        Directory.CreateDirectory(directory);
        var generatedPath = Path.Combine(directory, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(generatedPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new(generatedPath, variables, generatedPath);
    }

    private static JsonObject BuildServer(LocalMcpServerUpdate server, string processTokenVariable) => new()
    {
        ["type"] = "local",
        ["command"] = new JsonArray(server.Arguments.Prepend(server.Executable)
            .Select(argument => (JsonNode?)JsonValue.Create(argument)).ToArray()),
        ["enabled"] = server.Enabled,
        ["environment"] = new JsonObject
        {
            [server.TokenEnvironmentVariable] = $"{{env:{processTokenVariable}}}",
        },
    };

    private static void AddAllowedTools(JsonObject root, string agentName, string serverName, IReadOnlyList<string> tools)
    {
        if (root["agent"]?[agentName]?["permission"] is not JsonObject permission)
            throw new InvalidOperationException($"Agent '{agentName}' is missing a permission profile.");
        foreach (var tool in tools)
            permission[$"{serverName}_{tool}"] = "allow";
    }
}

public sealed class OpenCodeMcpLaunch(string configPath, IReadOnlyDictionary<string, string> variables, string? generatedPath) : IDisposable
{
    public void Apply(ProcessStartInfo process)
    {
        process.Environment["OPENCODE_CONFIG"] = configPath;
        foreach (var (key, value) in variables) process.Environment[key] = value;
    }

    public void Dispose()
    {
        if (generatedPath is null) return;
        try { File.Delete(generatedPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
