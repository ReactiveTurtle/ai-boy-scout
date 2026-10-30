---
id: ITR-015
topic: Move TodoGraph project identity and repository bindings behind EF/Application ports
type: implementation
state: proposed
---

## Expected result

TodoGraph project creation/listing/import identity uses an Application port backed by EF Core; projects with equal source IDs from distinct namespaces remain distinct internal projects.

## In scope

- Add the TodoGraph `Project` domain entity and repository-binding model.
- Add Application-owned project identity/repository ports and an identity-resolution use case.
- Implement EF Core project and repository-binding persistence against the existing SQLite file.
- Preserve legacy `(source_project_id, project path/name)` project rows and their exact repository bindings.
- Add EF integration tests for duplicate namespaces, legacy matching, and repository binding.

## Out of scope

- Moving every CandidateStore query to EF in this iteration.
- BoyScout database migration or Firebase adapter migration.
- Project editor UI in Angular.

## Primary risk

The current TodoGraph store already performs an ADO.NET project migration. The EF adapter must consume the same resulting schema without creating conflicting tables or rewriting active task/worktree state.

## Verification

- Infrastructure SQLite tests using explicit EF migrations.
- The existing 64 TodoGraph store tests remain green during the transition.
- Build the TodoGraph host to temporary output while port 5207 remains active.
