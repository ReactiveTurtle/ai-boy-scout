using System.Diagnostics;

namespace TodoGraph;

public interface ITaskWorktreePreparer
{
    Task<string> PrepareAsync(TaskNode task, CancellationToken cancellationToken);
}

public sealed class TaskWorktreePreparer : ITaskWorktreePreparer
{
    public async Task<string> PrepareAsync(TaskNode task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.RepositoryPath) || string.IsNullOrWhiteSpace(task.WorktreePath) ||
            string.IsNullOrWhiteSpace(task.BranchName))
            throw new InvalidOperationException("Task repository, worktree path or branch was not saved.");
        if (Directory.Exists(task.WorktreePath) || File.Exists(task.WorktreePath))
            throw new InvalidOperationException("The saved worktree path already exists. Inspect it before retrying.");

        var root = task.RepositoryPath;
        if (string.IsNullOrWhiteSpace(await GitAsync(root, ["remote", "get-url", "origin"], cancellationToken)))
            throw new InvalidOperationException("В репозитории не настроен Git remote origin.");

        await GitAsync(root, ["fetch", "--prune", "origin"], cancellationToken);
        var existingMrBranch = task.MergeRequestStatus == MergeRequestStatus.Open && !string.IsNullOrWhiteSpace(task.MergeRequestUrl)
            ? task.MergeRequestSourceBranch : null;
        var baseBranch = existingMrBranch ?? task.BaseBranch ?? await DetectBaseBranchAsync(root, cancellationToken);
        if (string.IsNullOrWhiteSpace(baseBranch))
            throw new InvalidOperationException("Не удалось определить базовую ветку по origin/HEAD или текущей ветке.");
        var baseRef = existingMrBranch is null ? baseBranch : $"origin/{existingMrBranch}";
        await GitAsync(root, ["rev-parse", "--verify", $"{baseRef}^{{commit}}"], cancellationToken);
        var worktreeList = await GitAsync(root, ["worktree", "list", "--porcelain"], cancellationToken);
        if (worktreeList.Split('\n').Any(line => line.Trim() == $"branch refs/heads/{task.BranchName}"))
            throw new InvalidOperationException($"Ветка «{task.BranchName}» уже открыта в другом worktree. У задачи должен быть ровно один worktree; сначала разберитесь с существующим checkout.");

        var existingBranch = await GitAsync(root, ["branch", "--list", task.BranchName], cancellationToken, optional: true);
        if (!string.IsNullOrWhiteSpace(existingBranch))
        {
            var localTip = await GitAsync(root, ["rev-parse", "--verify", $"{task.BranchName}^{{commit}}"], cancellationToken);
            var baseTip = await GitAsync(root, ["rev-parse", "--verify", $"{baseRef}^{{commit}}"], cancellationToken);
            if (!string.Equals(localTip.Trim(), baseTip.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Ветка «{task.BranchName}» уже существует на другом коммите. Проверьте её вручную; исполнитель не будет перезаписывать ветку.");
            await GitAsync(root, ["worktree", "add", task.WorktreePath, task.BranchName], cancellationToken);
        }
        else
            await GitAsync(root, ["worktree", "add", "-b", task.BranchName, task.WorktreePath, baseRef], cancellationToken);
        return baseBranch;
    }

    public static async Task<string> DetectBaseBranchAsync(string root, CancellationToken cancellationToken)
    {
        var baseBranch = await GitAsync(root, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], cancellationToken, optional: true);
        if (string.IsNullOrWhiteSpace(baseBranch))
            baseBranch = await GitAsync(root, ["branch", "--show-current"], cancellationToken);
        return baseBranch.Trim();
    }

    private static async Task<string> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken, bool optional = false)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Git could not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(linked.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Git worktree preparation exceeded one minute.");
        }
        var output = (await stdout).Trim();
        var errorOutput = (await stderr).Trim();
        if (process.ExitCode != 0 && !optional)
            throw new InvalidOperationException($"Git {arguments[0]} failed: {(errorOutput.Length > 1000 ? errorOutput[..1000] : errorOutput)}");
        return process.ExitCode == 0 ? output : string.Empty;
    }
}
