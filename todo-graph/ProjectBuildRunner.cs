using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TodoGraph;

public sealed record TaskBuildResult(string Command, int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IProjectBuildRunner
{
    Task<TaskBuildResult> BuildAsync(TaskNode task, TaskPreflight? preflight, CancellationToken cancellationToken);
    Task<bool> HasChangesAsync(TaskNode task, CancellationToken cancellationToken);
}

public sealed class ProjectBuildRunner : IProjectBuildRunner
{
    public async Task<TaskBuildResult> BuildAsync(TaskNode task, TaskPreflight? preflight, CancellationToken cancellationToken)
    {
        var root = task.WorktreePath ?? throw new InvalidOperationException("The worktree is missing.");
        var (executable, arguments) = DetectBuild(root, preflight?.Answers);
        string? dependencyOutput = null;
        string? dependencyCommand = null;
        if (executable == "cmd.exe" && File.Exists(Path.Combine(root, "package.json")))
        {
            dependencyCommand = File.Exists(Path.Combine(root, "package-lock.json")) || File.Exists(Path.Combine(root, "npm-shrinkwrap.json"))
                ? "npm ci" : "npm install --no-package-lock";
            var restore = await RunAsync(root, "cmd.exe", ["/d", "/s", "/c", dependencyCommand], cancellationToken, TimeSpan.FromMinutes(15));
            dependencyOutput = restore.Output;
            if (restore.ExitCode != 0)
                return new(dependencyCommand, restore.ExitCode, dependencyOutput);
        }
        var result = await RunAsync(root, executable, arguments, cancellationToken, TimeSpan.FromMinutes(15));
        var command = $"{executable} {string.Join(' ', arguments)}";
        return new(dependencyCommand is null ? command : $"{dependencyCommand} && {command}", result.ExitCode,
            dependencyOutput is null ? result.Output : dependencyOutput + Environment.NewLine + result.Output);
    }

    public async Task<bool> HasChangesAsync(TaskNode task, CancellationToken cancellationToken)
    {
        var root = task.WorktreePath ?? throw new InvalidOperationException("The worktree is missing.");
        var result = await RunAsync(root, "git", ["status", "--porcelain", "--untracked-files=normal"], cancellationToken, TimeSpan.FromSeconds(30));
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not inspect worktree changes.");
        return !string.IsNullOrWhiteSpace(result.Output);
    }

    internal static (string Executable, string[] Arguments) DetectBuild(string root, string? answers = null)
    {
        var solutions = Directory.EnumerateFiles(root, "*.sln").Concat(Directory.EnumerateFiles(root, "*.slnx")).ToArray();
        if (solutions.Length == 1) return ("dotnet", ["test", solutions[0]]);
        if (solutions.Length > 1)
        {
            var selected = solutions.Where(solution => answers?.Contains(Path.GetFileName(solution), StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (selected.Length == 1) return ("dotnet", ["test", selected[0]]);
            throw new InvalidOperationException($"В проекте несколько solution-файлов ({string.Join(", ", solutions.Select(Path.GetFileName))}). Укажите, какой собирать.");
        }
        var csproj = Directory.EnumerateFiles(root, "*.csproj").ToArray();
        if (csproj.Length == 1) return ("dotnet", ["build", csproj[0]]);
        if (csproj.Length > 1)
        {
            var selected = csproj.Where(project => answers?.Contains(Path.GetFileName(project), StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (selected.Length == 1) return ("dotnet", ["build", selected[0]]);
            throw new InvalidOperationException($"В проекте несколько .NET-файлов ({string.Join(", ", csproj.Select(Path.GetFileName))}). Укажите, какой собирать.");
        }
        var package = Path.Combine(root, "package.json");
        if (File.Exists(package))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(package));
            if (document.RootElement.TryGetProperty("scripts", out var scripts))
            {
                foreach (var script in new[] { "build", "test" })
                    if (scripts.TryGetProperty(script, out var command) && command.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(command.GetString()))
                        return OperatingSystem.IsWindows() ? ("cmd.exe", ["/d", "/s", "/c", $"npm run {script}"]) : ("npm", ["run", script]);
            }
        }
        if (File.Exists(Path.Combine(root, "go.mod"))) return ("go", ["test", "./..."]);
        var custom = answers?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("Команда сборки:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("build:", StringComparison.OrdinalIgnoreCase));
        if (custom is not null)
        {
            var command = custom[(custom.IndexOf(':') + 1)..].Trim();
            if (command.Length > 0 && !Regex.IsMatch(command, "[|;&<>`]"))
            {
                var tokens = Regex.Matches(command, "\"([^\"]*)\"|'([^']*)'|([^\\s]+)")
                    .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value)
                    .ToArray();
                if (tokens.Length > 0 && !tokens[0].EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) &&
                    tokens[0] is not ("cmd" or "cmd.exe" or "powershell" or "pwsh"))
                    return (tokens[0], tokens.Skip(1).ToArray());
            }
        }
        throw new InvalidOperationException("Не удалось однозначно определить команду сборки. Уточните её в поле «Команда сборки: <программа> <аргументы>».");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string root, string executable, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken, TimeSpan duration)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(duration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"{executable} exceeded the build timeout.");
        }
        var text = (await stdout) + Environment.NewLine + (await stderr);
        return (process.ExitCode, text.Length > 6000 ? text[^6000..] : text);
    }
}
