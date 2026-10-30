# ITR-002 - Add project mapping and UI queue

**Type:** implementation  
**State:** ready

## Topic

Let the user configure a local Git repository for a Todo Graph project, enqueue a task from its UI, and reject a task into `closed`.

## Expected result

After this iteration, project repository paths and queue/reject state are saved in Todo Graph; existing task data and statuses remain compatible. An unconfigured/non-Git project cannot be queued and shows a useful explanation. Confirm with store/API tests and an isolated runtime scenario.

## In scope

- Add an additive SQLite migration for per-project repository path and task execution/build state.
- Add loopback API routes to set a repository mapping, enqueue an eligible task, and close/reject a task.
- Add repository path, queue, and reject controls in the task inspector.
- Keep existing task source status intact except the explicit reject action; new execution/build state is separate.
- Make enqueue idempotent for a task already queued and refuse closed tasks.

## Out of scope

- Preflight explanation/clarification, worktree creation, code changes, build execution, commit, push, and MR creation.
- Decomposition child-task creation.

## Boundary scenarios

- A valid Git repository path is persisted per `SourceProjectId` and reused by its tasks.
- A path that is missing or not a Git repository is rejected without replacing the last valid mapping.
- Queueing requires a configured project repository and does not queue `closed`/`done` tasks.
- Repeated queue requests do not create duplicate work jobs.
- Rejecting a task sets it closed and prevents pending work from being claimed later.
- Existing databases gain nullable/defaulted fields without losing imported tasks, statuses, comments, or replies.

## User decisions

- Repository path is configured in Todo Graph project UI; execution uses a Git worktree.
- Full workflow continues through MR; the UI will surface execution phases and a separate build state.
- Build errors trigger diagnosis and repair without a fixed retry cap; an MR is not created until the build passes.
- Changes should be backward compatible; a breaking exception must be explained and approved.
- During implementation/retry cycles, the agent diagnoses before changing code and asks the user when the right fix is ambiguous; aim to resolve uncertainties before the first implementation pass.

## Result and verification

- Added additive SQLite columns for execution/build state and a project-to-Git-root mapping; existing data migrates without status loss.
- Added API/UI controls for repository mapping, idempotent task enqueue, explicit reject/close, and distinct execution/build indicators.
- Targeted Todo Graph tests passed: 23/23, including a real temporary Git repository and migration from a legacy task schema; JS syntax check passed.
- Isolated Todo Graph runtime check: missing mapping returned 409; after saving a real Git root, two queue calls returned 202 without duplicate state; `/api/graph` returned `queued` and `not_started`; rejecting returned `closed` and blocked queue with 409.
- Enqueue currently records durable intent; processing the intent into pre-development clarification is the next iteration and must be implemented before treating queueing as actual execution.
