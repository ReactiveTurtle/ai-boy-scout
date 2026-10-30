---
id: ITR-016
topic: Persist TodoGraph project identities and repository bindings through EF
type: implementation
state: verifying
---

## Expected result

TodoGraph Application resolves project identities through a storage-neutral port; EF Core persists an internal project ID, namespaced source identity, and repository binding in the existing SQLite database.

## In scope

- Add TodoGraph Project and RepositoryBinding domain entities.
- Add an Application `IProjectRepository` and source-identity resolution use case using `IUnitOfWork`.
- Map the existing `projects`/`project_repositories` tables through EF and add an idempotent migration for fresh databases.
- Test same numeric source ID across namespaces, same-source idempotency, and exact legacy-path adoption.
- Preserve existing project repository bindings and internal IDs produced by the current migration.

## Out of scope

- Replacing CandidateStore project reads/writes in this iteration.
- EF mapping for all task/worktree/MR columns; `ITaskRepository` is the application boundary, its EF adapter is a later iteration.
- Angular project editor/workspace changes.

## Primary risk

The current SQLite file may already contain project tables initialized by the legacy store; EF migration must be idempotent and must not recreate or reset them.

## Verification

- EF SQLite integration tests for project identity resolution and repository binding.
- Re-run the 64 TodoGraph store characterization tests and build the API to temporary output.

## Verification result

- `TodoGraph.Infrastructure.Tests.ProjectIdentityRepositoryTests`: 3/3 passed for namespace separation, same-namespace idempotency, legacy namespace adoption, local repository binding, and EF migration over the existing ADO-created schema.
- Existing `TodoGraphCandidateStoreTests`: 64/64 passed before the EF-backed list route was wired.
- `dotnet build todo-graph/TodoGraph.csproj --no-restore -p:OutDir=<approved-temp-dir>`: passed with 0 warnings/errors.
- Angular production build passed; it retains the existing `app.scss` budget warning (26.97 kB vs 26 kB).
- The live TodoGraph process was not stopped; runtime on port 5207 still needs restart to load the new EF/CORS behavior.

## Verification result

- `TodoGraph.Infrastructure.Tests.ProjectIdentityRepositoryTests`: 3/3 passed for duplicate namespaces, same-namespace idempotency, legacy namespace adoption, local repository binding, and EF migration over the existing ADO-created database schema.
- Existing `TodoGraphCandidateStoreTests`: 64/64 passed before the EF project list route was wired.
- `dotnet build todo-graph/TodoGraph.csproj --no-restore -p:OutDir=<approved-temp-dir>`: passed with 0 warnings/errors.
- Angular production build passed; it retains the pre-existing `app.scss` style-budget warning (26.97 kB vs 26 kB).
- The live TodoGraph process was not stopped; EF/CORS runtime behavior on port 5207 still requires a service restart.
- Temporary operational smoke check: started the newly built TodoGraph host on port 5217 with an isolated temp database; EF migrations and built-in process seeding completed, health returned ok, CORS preflight returned 204, and the EF project-list endpoint returned HTTP 200 with `[]`. Temporary process was stopped afterward.
