---
id: ITR-017
topic: Persist TodoGraphTask through the EF-backed Application port
type: implementation
state: verifying
---

## Expected result

`ITaskRepository` has an EF Core Infrastructure implementation that persists TodoGraphTask rows in the existing `task_nodes` table and remains substitutable in Application.

## In scope

- Map the core TodoGraphTask domain fields through EF to the existing task storage table.
- Add a legacy-safe EF migration for fresh databases without replacing an existing ADO-created `task_nodes` table.
- Verify EF-created tasks are readable by the existing workflow adapter during the transition.
- Keep process/task creation inside the existing unit-of-work transaction boundary.

## Out of scope

- Moving every execution/worktree/MR status column off CandidateStore in this iteration.
- Replacing the legacy CandidateStore for candidates, worktree operations, or history.
- Firebase implementation for TodoGraph; its repository port remains provider-neutral for future adapters.

## Primary risk

EF-inserted tasks must satisfy the current `task_nodes` schema defaults and preserve the relationships expected by the running task/worktree workers.

## Verification

- Infrastructure SQLite test creates a CandidateStore-compatible database, migrates it with EF, inserts via `ITaskRepository`, then queries the task through the legacy workflow adapter.
- Run focused Application and Infrastructure tests and build the TodoGraph API to temporary output.

## Verification result

- `TaskRepositoryTests`: passed 1/1. An EF-created task was read back through the existing CandidateStore workflow mapper with internal project ID, source IDs, criteria, and report link intact.
- The default test build was blocked by the active service PID 33620 locking `todo-graph/bin/Debug/net10.0/TodoGraph.dll`; no process was stopped. The same test passed when rerun with temporary output.
- Full `TodoGraph.Infrastructure.Tests`: 6/6 passed using temporary output.
- `TodoGraph.Application.Tests`: 2/2 passed; `TodoGraph.Domain.Tests`: 4/4 passed.
- TodoGraph API build to temporary output: 0 warnings/errors.
- Full `BoyScout.Core.Tests`: 365/365 passed using temporary output.
