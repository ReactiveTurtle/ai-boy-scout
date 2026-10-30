# ITR-011 - Use the existing MR source branch

**Type:** implementation  
**State:** ready

## Topic

For a task that already has an open MR, fetch its source branch from the configured read-only GitLab MCP and use that branch as the task's one worktree base.

## Expected result

Comment polling persists the MR source branch. Queue/preflight refuses to claim an existing-MR task until that branch is known. The worktree worker prepares exactly one worktree for the task from the source branch; a branch already checked out elsewhere is a visible blocker, never a reason to create a detached or duplicate worktree.

## Boundary scenarios

- New task without an MR -> unique task branch from detected base branch.
- Existing open MR -> source branch fetched/used; commit and manual push target that same branch.
- MR source branch unavailable or invalid -> block and explain; no default-branch worktree is created.
- Source branch already checked out by another worktree -> block and explain; do not force or detach.
- MR closes/merges before worktree creation -> do not start from that MR branch.

## User decision

One task owns exactly one worktree; a conflicting checkout is an incorrect state to surface to the user.

## Result and verification

- The GitLab review agent now returns MR state and source branch with comments; the store persists both separately from task/execution status.
- Queueing an existing open MR is blocked until the read MCP has fetched its source branch. Worktree preparation uses that branch; an already checked-out source branch creates a visible blocking error and no second/detached worktree.
- Tests passed for source-branch gating, MR state independent from `in_progress`, existing-MR worktree creation, and conflict refusal.
- Actual GitLab source-branch lookup is not yet tested without configured `gitlab` MCP.
