---
id: ITR-005
topic: Add the Project aggregate root and Common.Domain BaseEntity
type: implementation
state: verifying
---

## Expected result

TodoGraph.Domain contains a DDD `Project` aggregate root derived from `Common.Domain.AggregateRoot` (which derives from `BaseEntity`), with a stable internal `Guid` identity.

## Project principle

- `Project` is the aggregate root for a workspace; its identity is a GUID and is independent of its display name.
- The project name is required and trimmed; renaming changes the name without replacing identity.
- Repository paths, persistence, and external/import identifiers are not part of this initial entity; add them only with a concrete domain scenario.
- `Common.Domain` contains only shared domain primitives and has no infrastructure/framework dependencies.

## Scenarios

1. Creating a project with a valid name generates a non-empty GUID and stores the trimmed name.
2. Renaming preserves the GUID and normalizes the new name.
3. Empty or whitespace-only names are rejected at creation and rename boundaries.
4. Separate projects receive separate GUID identities.

## In scope

- Add `Common.Domain` with `BaseEntity`.
- Add the TodoGraph `Project` entity and minimal unit tests.
- Reference Common.Domain from TodoGraph.Domain and the Domain project from the blank host.

## Out of scope

- Persistence, repository binding, source/import identity, API routes, and application services.

## Verification

- Focused Project domain tests.
- Full solution build to temporary output.

## Verification result

- `TodoGraph.Domain.Tests.ProjectTests`: passed 6/6 for generated GUID identity, distinct project IDs, name trimming, rename identity stability, and empty-name rejection.
- Full `BoyScout.sln` build to temporary output: passed with 0 warnings/errors.
- `Project` and `BaseEntity` XML summaries document the aggregate-root and stable internal identity principles.
