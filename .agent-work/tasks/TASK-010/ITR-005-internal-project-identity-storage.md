---
id: ITR-005
topic: Persist internal Todo Graph project IDs and migrate project references
type: implementation
state: split
---

## Expected result

Two source databases importing the same numeric project ID produce separate internal Todo Graph projects; candidates, tasks, and repository configuration resolve through the internal project ID. Existing legacy rows are migrated without merging different stored project paths/names.

## In scope

- Add an internal project registry and repository binding keyed by internal project ID.
- Add source database namespace metadata to imported candidate/task records.
- Namespace imported report/task uniqueness keys internally while keeping original source IDs in API models.
- Resolve new imports by `(sourceNamespaceId, sourceProjectId)` and adopt matching legacy rows by exact source ID plus stored project path/name.
- Backfill legacy rows into separate internal projects grouped by `(source_project_id, project)`.
- Make project listing and project-scoped candidate/task/raw-item operations use internal IDs.
- Add focused tests for duplicate source IDs, legacy migration, task scoping, and repository binding.

## Out of scope

- Angular project workspace implementation.
- Rebuilding candidate idempotency around source report IDs unless the project migration proves it necessary.
- Automatically assigning an old repository binding when its existing project label does not match a single migrated project.
- Changes to unrelated Boy Scout/Firebase working-tree modifications.

## Primary risk

Existing task workers join repository settings through the current project ID column. Every internal-ID migration path must update those joins and preserve branch/worktree state.

## Verification

- Targeted Todo Graph store tests using isolated SQLite databases.
- Full `TodoGraphCandidateStoreTests` run to verify existing task/worktree/rebase/branch workflow behavior.
- Use a temporary build output directory while the live Todo Graph process holds the normal output DLL.

## Topic evolution

### Initial topic

Add internal Todo Graph project IDs and migrate project-scoped records.

### User direction

Use EF on the backend and clean architecture.

### Decision

Do not extend the in-progress ADO.NET migration. Replace the project identity persistence work with an EF Core implementation behind Domain/Application/Infrastructure boundaries, hosted by the Todo Graph API. The current ADO.NET edits in `CandidateStore.cs` and `Models.cs` are unverified draft changes and will be reviewed/reworked as part of the EF migration; they are not accepted as the final architecture.

### Split

- ITR-006 records the EF/Clean Architecture boundaries.
- The next implementation iteration begins with a failing behavior test for two source namespaces sharing one source project ID.
