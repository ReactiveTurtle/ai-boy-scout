# TASK-005 - Queue and execute Todo Graph tasks

## Goal

Let a user queue a task from Todo Graph's UI and track background implementation, MR-comment analysis, commit, and project-build progress.

## User-requested behavior

- Add an explicit UI action to put a task into the work queue.
- Show task phases such as queued, requirements clarification, development, MR-comment analysis, comment analysis complete, committing, and Code Review.
- Separately show whether the project is building and the build result.
- Keep Code Review MR polling integrated with the task workflow.
- Allow the user to reject a task; rejected tasks become `closed`.
- Execute the full implementation cycle through an MR, isolated in a Git worktree.
- Before code changes, clearly explain the problem, task interpretation, risks, and corner cases in plain language; an HTML page is acceptable.
- Ask the user for answers only where requirements are genuinely unresolved; do not quiz the user about whether they understood the explanation.
- After requirements are clarified, the agent may propose a task decomposition; create child tasks only after the user's approval.
- Prefer non-breaking, backward-compatible implementation. Explain and obtain approval for a necessary breaking exception before making it.
- If a build fails, investigate the root cause, fix it, and rebuild; do not open an MR while the build is failing.
- Continue build diagnosis/fix/rebuild attempts without a fixed retry limit; stop only for an external/blocking condition and report it.
- During development/retry cycles, analyze the error before changing code and ask the user when the cause or repair has multiple plausible interpretations; clear, low-risk fixes may proceed autonomously.
- Aim to resolve ambiguity and identify relevant edge cases before the first implementation attempt.

## Verified starting state

- Todo Graph originally had no task execution queue; its background worker only analyzed candidate links/task plans.
- `Project` was a display name passed from Boy Scout, not a checkout path.
- The existing task status selector did not enqueue work.
- The user selected full-cycle execution through an MR, Git worktree isolation, and rejection as the `closed` task state.
- Before development, the user requires a clear plain-language explanation of the problem, risks, and corner cases; an HTML page is acceptable.
- After requirements clarification, the agent may propose decomposition, but child tasks require user approval.
- Backward compatibility is the default; breaking exceptions must be explained and approved.
- Build failures trigger root-cause investigation and repair attempts; MR creation waits for a successful build.
- The user chose no fixed limit for build repair cycles.
- During each repair cycle, the agent should ask for clarification when the correct fix is ambiguous; the goal is to get the implementation right on the first attempt by resolving uncertainty early.
- The user chose automatic detection of the project build/check command and MR base branch from the repository; the pre-development explanation must show what was found.
- The user clarified that approval of the pre-development explanation authorizes implementation and a local commit after a successful build. The agent must ask the human to push the branch; it only verifies the committed SHA on the remote branch and creates an MR after verification. The agent must never push.
- MR membership/state is independent of the task's execution phase: the task may have an open MR while the agent is analyzing comments or preparing a new local commit. Poll by MR state and URL, not by the task status `code_review`.
- Existing open MR source branches are obtained through GitLab MCP; the worktree is based on that source branch. A branch already checked out in another worktree blocks execution because one task must own exactly one worktree.
- One task owns exactly one worktree. For an existing MR, read its source branch through GitLab MCP and use that branch as the worktree base. If another checkout already owns that branch, block and explain; never create a detached/duplicate worktree.
- The user clarified that manual approval is required for the initial transition from queue/clarification into development. Once the task has been started, new MR-comment cycles may be processed automatically.
- Each project selects GitLab or GitHub in Todo Graph settings. GitHub PR comments use a read-only GitHub MCP; the user pushes and creates GitHub PRs manually.
- Poll and display MR/PR approval and mergeability state; mark ready-to-merge only when the request is open, checks/build pass, and required approvals are present. Merging is always a human action.
- Workflow/build/MR/approval/mergeability/host states use enum types with stable string values at the API and SQLite boundaries.
- Manual task state changes are not exposed; the user starts implementation through the explicit **К разработке** action.
- Rebase new and existing MR branches onto the latest target branch before local commit, rebuild afterward, and stop for human conflict resolution.

## Remaining external configuration

- The website should configure local stdio `gitlab`, `gitlab-write`, and read-only `github` MCP servers in TASK-006/TASK-009; real provider operations remain unverified without them.
- Manual task creation and creator attribution are tracked in TASK-007.

## Risks

- Background code edits and repository commands have significant local/external side effects.
- A project display name is not a safe or reliable repository path; a worktree still needs a base checkout.
- Commits, pushes, and MR creation require explicit policy and idempotent recovery.

## Current boundary

Implemented in the working tree: per-project repository mapping; UI queue/reject; plain-language HTML preflight with detected build command/base branch, requirements questions, risks and corner cases; user answers and explicit approval; isolated worktree; code agent; automatic build diagnosis/repair; local commit; manual push request and remote SHA verification; separate MR state; comment-review cycles; human-approved replies; and MR lookup/creation after push verification. The agent never runs `git push`.

Verification: full solution tests passed (334/334); JavaScript syntax/OpenCode JSON/diff checks pass. Actual OpenCode preflight and a user-answer → repeat-analysis cycle ran against the local checkout, exposing a Russian explanation, questions, detected build command and base branch. Browser verification covered repository-path setup, enqueue and reject/close; a browser preflight check confirmed explanation/risk/corner-case content and that unresolved questions disable development approval. Deterministic tests cover MR source branch/worktree ownership, repair, local commit, manual-push verification and comment/reply state.

Remaining: actual GitLab MCP read/write operations and a real task's developer/build/commit/MR cycle have not been run with user credentials. Configure read-only `mcp.gitlab` and selectively allow only required tools under `mcp.gitlab-write`; push permissions are denied by default. The existing Todo Graph PID 33600 still has its old assembly loaded; rebuild and restart is required before using the new flow.
