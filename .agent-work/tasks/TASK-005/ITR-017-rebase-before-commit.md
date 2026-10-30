# ITR-017 - Rebase onto the latest target before commit

**Type:** implementation  
**State:** implementing  

## Topic

Rebase the task worktree branch onto the latest target branch immediately before local commit, then rerun the project build before allowing the commit.

## Expected result

Both newly created branches and existing MR/PR branches are fetched/rebased onto the latest target branch before local commit. A successful rebase invalidates the previous build result and causes a fresh build. A rebase conflict or unavailable target stops before commit and leaves the task blocked for human inspection/resolution.

## Boundary scenarios

- New branch with a remote target update: rebase onto the fetched target and rebuild before commit.
- Existing MR/PR branch: preserve its source branch and rebase that branch onto its configured target.
- No uncommitted task changes: rebase and rebuild normally.
- Uncommitted or untracked task changes: preserve them across the rebase; never discard them.
- Rebase conflict: do not commit or push; preserve Git's conflict state and report the exact local worktree for human resolution.
- Fetch/target-branch failure: stop before commit and report the failed prerequisite; do not silently use a stale target.
- Successful fresh build: permit the existing local-commit flow; push remains a human action.

## Progress

- The selected target-branch rule is to read the exact target branch from the linked MR/PR provider, not assume the repository default branch.
- Added `merge_request_target_branch` persistence and included `targetBranch` in the provider review response contract.
- Focused Todo Graph tests pass (42/42).
- Rebase execution, post-rebase rebuild, conflict handling, and their tests remain to be implemented.

## Verification

- Add deterministic Git repository tests for target advancement, new and existing branches, conflict stop, and required rebuild after rebase.
- Run the complete solution test suite using an output directory that does not collide with the running Todo Graph process.
