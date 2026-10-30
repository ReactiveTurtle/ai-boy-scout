---
id: ITR-008
topic: Characterize duplicate source-ID isolation before EF refactoring
type: test
state: accepted
---

## Expected result

Behavior tests prove distinct BoyScout namespaces with equal source project/report/task IDs yield distinct TodoGraph projects and independently scoped candidate/task/raw-item data, and that legacy paths remain separated during migration.

## In scope

- Add an end-to-end store test for two namespaces importing the same numeric project ID and the same report/task IDs.
- Assert unique internal project IDs, idempotent same-namespace re-import, and isolated candidate/task/raw-item queries.
- Add a migration test for legacy rows with one numeric project ID but different stored project paths, including exact repository-binding preservation.
- Use these as characterization tests while replacing ADO.NET persistence with EF Core.

## Out of scope

- EF architecture scaffolding or production-code changes in this test iteration.
- Guessing a mapping for legacy rows with identical numeric IDs and identical stored project identity but no namespace.
- Testing external OpenCode/GitLab/GitHub providers.

## Primary risk

The current ADO.NET draft has not yet been validated and existing tests contain old assumptions that a locally-created project's ID is negative/upstream-shaped.

## Verification

The two new tests pass against the current unverified ADO.NET draft when built to temporary output. The first run exposed a fixture path-escaping mismatch (stored path contained doubled separators); the fixture was corrected and both tests then passed. The full `TodoGraphCandidateStoreTests` suite remains to be run after the EF refactor.
