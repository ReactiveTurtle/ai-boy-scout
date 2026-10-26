using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TodoGraph;

public interface ITaskGitOperations
{
    Task RebaseAsync(TaskNode task, CancellationToken cancellationToken);
    Task<string> CommitAsync(TaskNode task, CancellationToken cancellationToken);
    Task<bool> IsPushedAsync(TaskNode task, CancellationToken cancellationToken);
}

public sealed class TaskGitOperations : ITaskGitOperations
{
    private static readonly Regex SecretText = new("-----BEGIN [A-Z ]*PRIVATE KEY-----|(?i:(?:api[_-]?key|access[_-]?token|password|client[_-]?secret)\\s*[:=]\\s*['\"]?[A-Za-z0-9+/=_-]{12,})", RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, string>? gitEnvironment;

    public TaskGitOperations() { }

    internal TaskGitOperations(IReadOnlyDictionary<string, string> gitEnvironment) => this.gitEnvironment = gitEnvironment;

    public async Task RebaseAsync(TaskNode task, CancellationToken cancellationToken)
    {
        var root = task.WorktreePath ?? throw new InvalidOperationException("The task worktree is missing.");
        try
        {
            if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(task.BranchName))
                throw new InvalidOperationException("The task worktree or saved branch is missing.");
            var branch = (await RequiredGit(root, ["branch", "--show-current"], cancellationToken)).Trim();
            if (branch != task.BranchName)
                throw new InvalidOperationException("The worktree branch changed. Inspect it before rebasing.");
            var target = task.MergeRequestStatus == MergeRequestStatus.Open
                ? task.MergeRequestTargetBranch
                : task.BaseBranch;
            if (string.IsNullOrWhiteSpace(target) || target.StartsWith("-", StringComparison.Ordinal) ||
                (await RunGit(root, ["check-ref-format", "--branch", target], cancellationToken)).ExitCode != 0)
                throw new InvalidOperationException("The saved rebase target branch is missing or invalid.");
            _ = await RequiredGit(root, ["remote", "get-url", "origin"], cancellationToken);
            _ = await RequiredGit(root, ["fetch", "origin", $"refs/heads/{target}"], cancellationToken);
            _ = await RequiredGit(root, ["rebase", "--autostash", "FETCH_HEAD"], cancellationToken);
            var unresolvedPaths = (await RequiredGit(root, ["diff", "--name-only", "--diff-filter=U"], cancellationToken)).Trim();
            if (unresolvedPaths.Length > 0)
                throw new InvalidOperationException($"Rebase or autostash left unresolved conflicts: {unresolvedPaths}. Resolve them in the worktree before resuming.");
        }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not rebase task branch onto its saved target. Inspect and resolve the worktree at '{root}', then resume the task. No local commit was created. {error.Message}", error);
        }
    }

    public async Task<string> CommitAsync(TaskNode task, CancellationToken cancellationToken)
    {
        var root = task.WorktreePath ?? throw new InvalidOperationException("The task worktree is missing.");
        if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(task.BranchName) || task.BuildStatus != ProjectBuildStatus.Passed)
            throw new InvalidOperationException("Only a successfully built task with an existing worktree can be committed.");

        var branch = await RequiredGit(root, ["branch", "--show-current"], cancellationToken);
        if (branch.Trim() != task.BranchName)
            throw new InvalidOperationException("The worktree branch changed. Inspect it before committing.");
        _ = await RequiredGit(root, ["status", "--porcelain"], cancellationToken);
        _ = await RequiredGit(root, ["log", "--oneline", "-10"], cancellationToken);
        _ = await RequiredGit(root, ["diff", "--check"], cancellationToken);
        var diff = await RequiredGit(root, ["diff", "HEAD"], cancellationToken);
        var tracked = await RequiredGit(root, ["diff", "--name-only", "-z", "HEAD"], cancellationToken);
        var untracked = await RequiredGit(root, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken);
        var files = tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Concat(untracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("The task produced no changes to commit.");
        if (files.Length > 200 || files.Any(IsSensitivePath) || diff.Length > 1_000_000 || SecretText.IsMatch(diff))
            throw new InvalidOperationException("The change contains too many files or possibly sensitive content. Inspect it manually before committing.");

        var add = new List<string> { "add", "-A", "--" };
        add.AddRange(files);
        _ = await RequiredGit(root, add, cancellationToken);
        _ = await RequiredGit(root, ["diff", "--cached", "--check"], cancellationToken);
        var staged = await RequiredGit(root, ["diff", "--cached"], cancellationToken);
        if (staged.Length > 1_000_000 || SecretText.IsMatch(staged))
            throw new InvalidOperationException("Staged changes may contain sensitive content. Inspect them manually before committing.");
        var message = $"Implement task {task.SourceTaskId ?? task.Id.ToString()}: {string.Join(' ', task.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))}";
        if (message.Length > 100) message = message[..97] + "...";
        _ = await RequiredGit(root, ["commit", "-m", message], cancellationToken);
        var sha = (await RequiredGit(root, ["rev-parse", "HEAD"], cancellationToken)).Trim();
        if (!Regex.IsMatch(sha, "^[a-fA-F0-9]{40,64}$")) throw new InvalidOperationException("Git returned an invalid commit SHA.");
        return sha;
    }

    public async Task<bool> IsPushedAsync(TaskNode task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.RepositoryPath) || string.IsNullOrWhiteSpace(task.BranchName) ||
            string.IsNullOrWhiteSpace(task.CommitSha)) return false;
        var result = await RunGit(task.RepositoryPath, ["ls-remote", "--heads", "origin", $"refs/heads/{task.BranchName}"], cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not inspect the remote branch. Check Git access and try again.");
        var first = result.Output.Split('\t', 2)[0].Trim();
        return string.Equals(first, task.CommitSha, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSensitivePath(string path)
    {
        var file = Path.GetFileName(path).ToLowerInvariant();
        return file is ".env" or "id_rsa" or "id_ed25519" || file.StartsWith(".env.", StringComparison.Ordinal) ||
            file.EndsWith(".pem", StringComparison.Ordinal) || file.EndsWith(".key", StringComparison.Ordinal) ||
            file.EndsWith(".p12", StringComparison.Ordinal) || file.EndsWith(".pfx", StringComparison.Ordinal) ||
            file.Contains("secret", StringComparison.Ordinal) || file.Contains("credential", StringComparison.Ordinal);
    }

    private async Task<string> RequiredGit(string root, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var result = await RunGit(root, arguments, token);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Git {arguments[0]} failed: {result.Error[..Math.Min(result.Error.Length, 1000)]}");
        return result.Output;
    }

    private async Task<(int ExitCode, string Output, string Error)> RunGit(string root, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (gitEnvironment is not null)
            foreach (var (key, value) in gitEnvironment) start.Environment[key] = value;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Git could not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, token);
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (token.IsCancellationRequested) throw;
            throw new TimeoutException("Git operation timed out.");
        }
        return (process.ExitCode, await stdout, await stderr);
    }
}
