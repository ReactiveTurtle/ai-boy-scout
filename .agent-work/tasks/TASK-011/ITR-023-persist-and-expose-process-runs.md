---
id: ITR-023
topic: Persist and expose ProjectTask ProcessRuns
type: implementation
state: verifying
---

## Expected result

Starting a Process on a ProjectTask persists its immutable snapshot and runtime state; stage results and user responses survive API restart.

## Expected API scenarios

1. `POST /api/projects/{projectId}/tasks/{taskId}/process-run` with `{ processId }` starts the selected global Process and returns run status/current position; snapshot contents stay server-side.
2. A second start on the same ProjectTask returns a conflict; the one-run-only rule remains enforced after reload.
3. `GET /api/projects/{projectId}/tasks/{taskId}/process-run` returns status, current block/stage, prompt/error, and task title/description without exposing raw hashes as UI identifiers.
4. `POST /api/projects/{projectId}/tasks/{taskId}/process-run/current-stage-result` accepts `{ title, description, userPrompt }`, advances sequentially, and persists Blocked/Completed state and link counters.
5. `POST /api/projects/{projectId}/tasks/{taskId}/process-run/user-response` accepts `{ response }`; a waiting stage resumes at the same stage and the pending response survives API restart.
6. Unknown projects/tasks/processes return 404; duplicate starts or invalid run transitions return 409/400 without partial updates.

## In scope

- Persist one ProcessRun per ProjectTask in normalized tables: run state, ordered Block/Stage references, a stage snapshot store keyed by SHA-256, and per-link cycle counters.
- Add repository/Application commands and API routes for start, status, stage result, and user response.
- Verify restart persistence using an isolated database.

## Out of scope

- AI invocation/worker and frontend implementation.
- Parallel branches, predicates, cross-ProcessBlock links, or retry/restart after the single run.

## Storage decision — confirmed

- The user chose normalized tables.
- `process_runs` has one unique row per ProjectTask and stores current status/position/prompt/error.
- `process_run_blocks` and `process_run_stages` retain ordered snapshot references; they do not foreign-key to live Process definitions.
- `process_stage_snapshots` is keyed by SHA-256 and stores immutable stage content; `process_run_link_counters` stores counts per run/source StageId.
- Snapshot references do not depend on live Process/Block/Stage rows, so catalog edits cannot alter active runs.

## Verification result

- Added normalized `process_runs`, ordered block/stage reference, SHA-256 stage snapshot, and per-cycle counter tables.
- Added ProcessRun repository, Application handlers, and API routes for one-shot start, status, stage result, and user response.
- Release solution build passed with 0 warnings and 0 errors.
- Operational upgrade started from the previous API's Project/ProjectTask database. Both Process catalog and ProcessRun migrations applied; existing project/task rows survived.
- A separate migration-upgrade smoke started from the previously published Project/ProjectTask-only API database; the new API applied `AddProcessRunPersistence`, preserved the existing task, and started a ProcessRun on it.
- Started a run, persisted a wait prompt, restarted the API, submitted free text, resumed the same stage, completed the sequential run, and verified the task Title/Description after reload.
- A separate run followed a backward link to its per-cycle limit, became Blocked with an error, and retained that state after API restart. A second start returned HTTP 409 before and after API restart.
- Temporary API checks used isolated databases and ports 5235/5236/5237; all hosts were stopped and listeners released. No AI invocation or UI interaction was tested.
