---
id: ITR-004
topic: Publish the Boy Scout database namespace with Todo Graph imports
type: implementation
state: accepted
---

## Expected result

Every report delivered to Todo Graph carries the stable installation ID of the Boy Scout database that assigned its project ID, separately from the report's originating installation metadata.

## In scope

- Expose `projectNamespaceId` on Boy Scout report API models using the local database installation ID.
- Include it in the Todo Graph publisher request.
- Add a focused test that observes the serialized publication payload.

## Out of scope

- Persisting or resolving the namespace in Todo Graph; that is a separate iteration.
- Changing report-origin metadata, project/task IDs, or repository mappings.
- Editing unrelated existing working-tree changes.

## Primary risk

Confusing the database namespace with `SourceInstallationId`, which identifies who originated a report and may differ for Firebase-backed reports.

## Verification

- `Todo_graph_publication_includes_source_database_namespace`: passed.
- `BoyScoutDatabaseTests`: 119/119 passed.
- The initial default-output test attempt was blocked when the active Todo Graph process (PID 33620 on port 5207) locked `todo-graph/bin/Debug/net10.0/TodoGraph.dll`. No process was terminated; rerunning to the approved temporary output directory passed both test runs.
- The namespace is emitted as `sourceNamespaceId`; the receiving Todo Graph model/storage does not consume it yet and is the next separate iteration.
