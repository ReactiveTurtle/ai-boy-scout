---
id: ITR-001
topic: Inventory TodoGraph surfaces and define safe retirement boundaries
type: discovery
state: verifying
---

## Expected result

TodoGraph-owned files, BoyScout integration points, database state, project references, and active-process constraints are identified before destructive edits.

## Verified facts

- `todo-graph/` contains the legacy ASP.NET Core host and vanilla UI; its files have uncommitted changes.
- `src/TodoGraph.Domain`, `src/TodoGraph.Application`, `src/TodoGraph.Infrastructure`, `tests/TodoGraph.*`, and `web-ui/src/app/todo-graph` are separate TodoGraph-owned directories.
- BoyScout integrates TodoGraph through `ApprovedReportPublication`, `TodoGraphPublisher`, MCP status updates, report delivery fields/status filters, and corresponding smoke/unit tests.
- `BoyScout.Common.Persistence.IUnitOfWork` is shared by BoyScout and must remain; only the TodoGraph integration contract is removed.
- The BoyScout schema is currently version 16. Migrations 13 and 15 are historical TodoGraph-delivery migrations; version 14 (Git worktrees) and version 16 (Firebase note markers) are unrelated and must remain.
- A TodoGraph process owns port 5207 (PID 33620). Do not stop it automatically; verify whether it blocks removing the old application output before deleting that directory.

## Decision

- Preserve BoyScout's historical migration sequence and add a forward migration that removes the obsolete TodoGraph delivery table; do not rewrite migrations already applied to user databases.
- Do not inspect or delete the TodoGraph database under LocalApplicationData; it is outside the repository and was not included in the confirmed folder deletion.
- Recreate the repository's `todo-graph/` as a blank ASP.NET Core web project after the old host can be removed safely.

## Verification

- Discovery performed using repository-local file search, solution/project-reference inspection, and a read-only check of the listener on port 5207.
