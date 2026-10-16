using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoyScout.Api.Setup;

public sealed record SetupFile(string Path, string Action);
public sealed record SetupPreview(string Repository, string McpDll, string SiteUrl, IReadOnlyList<SetupFile> Files, string Warning, string Token);

public sealed class RepositoryInstaller
{
    private readonly object gate = new();
    private readonly string? mcpDll;
    private const string McpName = "boy-scout";

    public RepositoryInstaller(string? mcpDll = null) => this.mcpDll = mcpDll;

    public SetupPreview Preview(string? repository)
    {
        lock (gate) return Prepare(repository).Preview;
    }

    public SetupPreview Install(string? repository, string? token)
    {
        lock (gate)
        {
            var plan = Prepare(repository);
            if (string.IsNullOrWhiteSpace(token) || token != plan.Preview.Token)
                throw new InvalidOperationException("Setup changed since preview. Preview again before confirming.");

            foreach (var file in plan.Writes)
            {
                if (file.Path == plan.ExcludePath)
                    File.AppendAllText(file.Path, file.Content, new UTF8Encoding(false));
                else
                    File.WriteAllText(file.Path, file.Content, new UTF8Encoding(false));
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(plan.ShortcutPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return plan.Preview;
        }
    }

    private Plan Prepare(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || !Path.IsPathFullyQualified(repository))
            throw new ArgumentException("Select an absolute local Git repository path.");
        var root = Path.GetFullPath(repository);
        var gitRoot = Directory.Exists(root) ? Git(root, "rev-parse", "--show-toplevel") : null;
        if (gitRoot is null || !string.Equals(Path.GetFullPath(gitRoot), root, PathComparison))
            throw new ArgumentException("Path must be the root of an existing Git repository.");

        var exclude = Git(root, "rev-parse", "--git-path", "info/exclude");
        if (exclude is null) throw new ArgumentException("Unable to locate Git local exclude file.");
        exclude = Path.GetFullPath(Path.IsPathRooted(exclude) ? exclude : Path.Combine(root, exclude));
        if (!Directory.Exists(Path.GetDirectoryName(exclude)))
            throw new InvalidOperationException("Git info directory is missing.");

        var dll = mcpDll ?? FindMcpDll();
        if (dll is null || !File.Exists(dll))
            throw new InvalidOperationException("Published artifacts/mcp/BoyScout.Mcp.dll is missing. Publish the MCP first.");
        var configPath = Path.Combine(root, "opencode.json");
        var shortcutName = OperatingSystem.IsWindows() ? "boy-scout.cmd" : "boy-scout.sh";
        var shortcutPath = Path.Combine(root, shortcutName);
        if (IsTracked(root, "opencode.json") || IsTracked(root, shortcutName))
            throw new InvalidOperationException("Setup files are tracked by Git. Refusing to modify files that could be committed.");
        if (Directory.Exists(configPath) || Directory.Exists(shortcutPath) || Directory.Exists(exclude))
            throw new InvalidOperationException("A setup file path is a directory.");
        if ((File.Exists(configPath) && File.GetAttributes(configPath).HasFlag(FileAttributes.ReparsePoint)) ||
            (File.Exists(exclude) && File.GetAttributes(exclude).HasFlag(FileAttributes.ReparsePoint)))
            throw new InvalidOperationException("Setup files must not be symbolic links.");

        var originalConfig = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        JsonObject config;
        try { config = originalConfig is null ? new JsonObject { ["$schema"] = "https://opencode.ai/config.json" } : JsonNode.Parse(originalConfig) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException("opencode.json must be a JSON object; no changes made."); }
        if (config.ContainsKey("mcp") && config["mcp"] is not JsonObject)
            throw new InvalidOperationException("opencode.json has a conflicting mcp property.");
        var mcp = config["mcp"] as JsonObject ?? new JsonObject();
        if (mcp.ContainsKey(McpName))
            throw new InvalidOperationException("opencode.json already defines boy-scout MCP. Resolve it manually before setup.");
        mcp[McpName] = new JsonObject
        {
            ["type"] = "local",
            ["command"] = new JsonArray("dotnet", dll),
            ["enabled"] = true
        };
        if (!config.ContainsKey("mcp")) config["mcp"] = mcp;
        var updatedConfig = config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

        var siteUrl = "http://127.0.0.1:5187/?projectPath=" + Uri.EscapeDataString(root);
        var shortcut = OperatingSystem.IsWindows()
            ? "@echo off\r\nstart \"\" \"" + siteUrl.Replace("%", "%%") + "\"\r\n"
            : "#!/bin/sh\nexec xdg-open '" + siteUrl.Replace("'", "'\\''") + "'\n";
        if (File.Exists(shortcutPath))
            throw new InvalidOperationException($"{shortcutName} already exists. Refusing to overwrite it.");

        var originalExclude = File.Exists(exclude) ? File.ReadAllText(exclude) : "";
        var entries = originalExclude.Split('\n').Select(line => line.Trim().TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        var additions = new[] { "/opencode.json", "/" + shortcutName }.Where(entry => !entries.Contains(entry)).ToArray();
        var excludeAddition = additions.Length == 0 ? "" : (originalExclude.Length > 0 && !originalExclude.EndsWith('\n') ? "\n" : "") + string.Join("\n", additions) + "\n";

        var writes = new List<Write> { new(configPath, updatedConfig), new(shortcutPath, shortcut) };
        if (excludeAddition.Length > 0) writes.Add(new Write(exclude, excludeAddition));
        var files = new List<SetupFile> {
            new(configPath, originalConfig is null ? "Create local OpenCode config with boy-scout MCP" : "Add boy-scout MCP to existing config (JSON formatting may change)"),
            new(shortcutPath, "Create local shortcut to common site"),
        };
        if (excludeAddition.Length > 0) files.Add(new SetupFile(exclude, "Append " + string.Join(", ", additions) + " to local Git exclude"));
        var fingerprint = string.Join("\0", root, dll, originalConfig ?? "<missing>", File.Exists(shortcutPath).ToString(), originalExclude, siteUrl);
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        var preview = new SetupPreview(root, dll, siteUrl, files,
            "If BoyScout MCP is already configured globally in OpenCode, remove or disable the global entry manually to avoid duplicate servers. This setup does not alter global config, grant AI access, start the site, or add secrets.", token);
        return new Plan(preview, exclude, shortcutPath, writes);
    }

    private static string? FindMcpDll()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "artifacts", "mcp", "BoyScout.Mcp.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsTracked(string root, string name) => Git(root, "ls-files", "--error-unmatch", "--", name) is not null;

    private static string? Git(string root, params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record Write(string Path, string Content);
    private sealed record Plan(SetupPreview Preview, string ExcludePath, string ShortcutPath, IReadOnlyList<Write> Writes);
}
