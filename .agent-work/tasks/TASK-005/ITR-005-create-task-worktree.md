# ITR-005 - Create an isolated task worktree

**Type:** implementation  
**State:** ready

## Topic

Claim an explicitly approved task and create a unique local Git worktree from the repository's automatically detected base branch without touching the user's checkout.

## Expected result

A prepared worktree path and branch are stored on the task. A failed Git operation becomes a visible blocked state; existing worktrees are never deleted or overwritten. Verify against a temporary local Git repo and an isolated service.

## Decision

- Prefer `refs/remotes/origin/HEAD` as the base; otherwise use the checked-out branch if it has a clean local name.
- Require an `origin` remote before starting work destined for an MR; only the human may push it later.
- If the task has an existing open MR, use its source branch; otherwise use origin/HEAD or the checked-out default branch.
- Use a unique branch and sibling worktree per task attempt; store both before the worktree operation for recovery.
- One task owns one worktree. If the MR source branch is already checked out elsewhere, block and ask the user to resolve the conflicting checkout; no detached or duplicate worktree.
- On interrupted preparation, preserve any worktree and show a blocked state requiring human investigation rather than creating a second copy.

## Boundary

- No source edits, builds, commits, pushes, or MR creation in this iteration.
- No deletion of worktrees or user data during cleanup.

## Adjacent requirement clarified

The user clarified that an MR can already exist while a task continues development or is being committed. A separate MR state is necessary; migrate legacy `code_review` tasks with MR URLs to `open` MR state without dropping the old workflow status. Apply this as a separate model iteration before claiming MR comment polling is correct.

## Result and verification

- A background worker reserves a unique branch and sibling worktree path, detects `origin/HEAD` or the checked-out branch, verifies a Git commit, and creates a worktree; it does not edit, commit, or push.
- Interruption preserves the reserved path and blocks automatic retries. If the path was never created, an explicit retry can reuse it; existing paths are never deleted or overwritten.
- Targeted tests used real temporary Git checkouts/origins: creation left the remote empty; missing origin blocked and retry succeeded after adding one.
