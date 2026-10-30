---
id: ITR-022
topic: Persist and expose the global Process catalog
type: implementation
state: verifying
---

## Expected result

Process definitions persist in TodoGraph's SQLite database and are editable through minimal API routes, preserving Process → ordered ProcessBlocks → ordered ProcessStages.

## Scenarios

1. `POST /api/processes` with `{ name, initialStageInstructions }` creates a global Process with one Block and one Stage; `GET /api/processes` and `GET /api/processes/{processId}` return the full ordered definition.
2. Process rename, block/stage add/remove, and block/stage reorder operations persist without replacing the IDs of retained entities.
3. `PUT /api/processes/{processId}/blocks/{blockId}/stages/{stageId}/backward-link` sets or replaces a positive-limit link to an earlier Stage in that same Block; `DELETE` removes it.
4. Invalid text, duplicate/missing/foreign IDs, invalid order, cross-block/forward links, or non-positive limits are rejected without partial mutation. Unknown resource IDs return 404; attempts to remove the final Block/Stage return 409.
5. API restart preserves process names, block/stage order, instructions, and backward-link settings.
6. Existing Project and ProjectTask data remain readable after the additive migration.

## In scope

- Add EF mappings/migration and a Process repository.
- Add Application commands/queries for Process definitions.
- Add minimal API catalog and editing endpoints listed in the scenarios.
- Keep Process definitions global and independent of Projects.

## API contract

- `GET /api/processes` and `GET /api/processes/{processId}` return the ordered Process → Blocks → Stages hierarchy.
- `POST /api/processes` accepts `{ name, initialStageInstructions }`.
- `PUT /api/processes/{processId}` renames a Process with `{ name }`.
- `POST /api/processes/{processId}/blocks` accepts `{ initialStageInstructions }`; `DELETE` removes a block; `PUT /blocks/order` accepts `{ blockIds }`.
- `POST /api/processes/{processId}/blocks/{blockId}/stages` accepts `{ instructions }`; `PUT /stages/{stageId}` updates instructions; `DELETE` removes a stage; `PUT /stages/order` accepts `{ stageIds }`.
- `PUT /stages/{stageId}/backward-link` accepts `{ targetStageId, maxRepeats }`; `DELETE` removes the link.
- Successful definition edits return the updated Process DTO; catalog creation returns `201 Created`.

## Out of scope

- ProcessRun persistence/start/result/wait API (ITR-023).
- Angular UI implementation; the current design handoff is the target.
- AI invocation and SignalR Process events.
- Deleting a Process definition.

## Primary risk

Relational persistence must preserve in-memory collection order and keep stage-back-link invariants valid when stages are reordered or removed.

## Verification

- Build the TodoGraph solution and apply the migration to an isolated temporary database.
- Operationally exercise create/read/edit/reorder/back-link and invalid-boundary scenarios, restart the API, then confirm data survives.

## Verification result

- Added ordered EF Core tables for Processes, Blocks, and Stages, including optional same-block backward-link data; existing Project/ProjectTask mappings remain intact.
- Added Process repository, catalog DTO/handler, and Process catalog/editing minimal API routes.
- Release solution build passed with 0 warnings and 0 errors.
- `dotnet ef migrations add` succeeded; the installed EF tool is 9.0.5 while the project runtime is 10.0.10, so tooling emitted its version warning.
- An isolated upgrade started from the previous API's Project/ProjectTask schema. The new API applied the Process migration, preserved existing Project/Task rows, then created and edited a Process with ordered blocks/stages and a backward link.
- Process rename, block/stage add/remove/reorder, stage update, and link set/update/remove passed. Blank text, duplicate order, forward links, and non-positive repeat counts returned HTTP 400; unknown Process/cross-block target returned HTTP 404; removing the last block/stage returned HTTP 409.
- Process order, stage instructions, and link limits survived API restart.
- APIs were tested on temporary ports 5232/5233 with a temporary database; both processes were stopped and their listeners released. The user-local database was not opened.
- ProcessRun persistence/start/result/wait endpoints remain ITR-023.
