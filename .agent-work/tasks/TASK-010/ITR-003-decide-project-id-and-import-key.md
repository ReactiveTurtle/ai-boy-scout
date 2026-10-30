---
id: ITR-003
topic: Define internal project ID and import-key mapping
type: decision
state: accepted
---

## Expected result

Define a stable internal project ID while preserving the upstream Boy Scout project identifier separately.

## Decision

- Todo Graph assigns each project a unique internal `ProjectId`; Angular routes and all project-scoped API operations use that ID.
- Imported records are resolved by `(source database installation ID, source project ID)`. The source database installation ID is separate from `BoyScoutReport.SourceInstallationId`, which identifies the report origin and can refer to a different installation for Firebase-backed reports.
- The Boy Scout API exposes the database namespace in report payloads; `TodoGraphPublisher` carries it for the report and its project tasks.
- Legacy records without a namespace are migrated using the existing `(source_project_id, project)` values to keep distinct path/name groups separate. Existing records that cannot be disambiguated are not merged automatically; their project/repository binding remains unresolved until explicitly mapped.
- Repository paths, task edges, source report links, and workflow state must be preserved while moving records to internal IDs.

## Human direction

The user directed project identity to be based on an ID (“по id почему нет”). This iteration records the concrete meaning needed to avoid the observed collision: the internal Todo Graph project ID is authoritative; the upstream numeric ID remains source metadata.

## Verification

The source trace confirms the upstream numeric project ID is database-local, and source database installation identity is available in Boy Scout configuration. No data was changed in this decision iteration.
