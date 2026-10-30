# ITR-019 - Rebase and rebuild before local commit

**Type:** implementation  
**State:** verifying

## Topic

Rebase the task branch onto the latest target branch immediately before local commit, then rerun the project build before allowing that commit.

## Expected scenarios

- New branch: fetch and rebase onto its detected target branch, rebuild, then permit a local commit.
- Existing open MR/PR branch: use the provider's saved target branch, not the MR source branch or an assumed default branch.
- Uncommitted task edits survive rebase; no content is discarded.
- Fetch/target failure or rebase conflict blocks the task before commit, leaves the worktree for human inspection, and reports its path.
- Build failure after rebase blocks the commit with the fresh build result.
- Human push remains manual; the agent does not push or merge.

## Verification

- Deterministic Git tests cover advancing target branch, existing-MR target selection, preserved local edits, conflict stop, and rebuild-before-commit.
- Run the solution tests using a temporary output directory while the service is running.
- Perform an operational isolated Git repository scenario through the worker path and verify the resulting commit is based on the fetched target and build ran after rebase.

## Implementation and results

- `TaskGitOperations.RebaseAsync` validates the checked-out branch and target, fetches `origin/<target>`, rebases with autostash, then checks for unresolved paths left by reapplying the autostash. Any failure stops the worker with the worktree path in the error.
- `TaskCommitWorker` invalidates the pre-rebase build result, rebases, rebuilds, and commits only if that fresh build passes. Fetch/rebase conflicts and post-rebase build failures block before commit; push remains manual.
- Tests cover a new branch with an advanced target and preserved staged/unstaged/untracked changes; an existing MR uses its saved target even when the worktree base/source differs; missing target fetch blocks; an autostash conflict leaves the worktree conflict state and blocks without rebuilding or committing; blocked pre-rebase failures invalidate the old build status.
- `dotnet test BoyScout.sln --no-restore -p:BaseOutputPath="C:\Users\DENIS~1.AKT\AppData\Local\Temp\opencode\BoyScoutTest\bin\"` passed: **348/348**.
- `dotnet build todo-graph/TodoGraph.csproj --no-restore` passed with 0 warnings/errors; service restarted on port 5207.
- The worker scenario uses real isolated local/bare Git repositories and a deterministic fake build runner. Real credentialed GitLab/GitHub MCP behavior remains unverified.
