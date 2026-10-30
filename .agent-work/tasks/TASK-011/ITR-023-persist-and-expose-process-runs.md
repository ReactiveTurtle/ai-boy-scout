---
id: ITR-023
topic: Persist and expose ProjectTask ProcessRuns
type: implementation
state: proposed
---

## Expected result

Starting a Process on a ProjectTask persists its immutable snapshot and runtime state; stage results and user responses survive API restart.

## Expected API scenarios

1. `POST /api/projects/{projectId}/tasks/{taskId}/process-run` with `{ processId }` starts the selected global Process and returns its captured snapshot/current position.
2. A second start on the same ProjectTask returns a conflict; the one-run-only rule remains enforced after reload.
3. `GET /api/projects/{projectId}/tasks/{taskId}/process-run` returns status, current block/stage, prompt/error, and task title/description without exposing raw hashes as UI identifiers.
4. A stage-result submission advances sequentially, updates ProjectTask, follows a back-link within its per-cycle limit, and persists Blocked/Completed state.
5. A waiting result persists its prompt; free-text response resumes the same stage; the response and next state survive API restart.
6. Unknown projects/tasks/processes return 404; duplicate starts or invalid run transitions return 409/400 without partial updates.

## In scope

- Select a storage mapping for ProcessRun state and immutable stage snapshots.
- Add repository/Application commands and API routes for start, status, stage result, and user response.
- Verify restart persistence using an isolated database.

## Out of scope

- AI invocation/worker and frontend implementation.
- Parallel branches, predicates, cross-ProcessBlock links, or retry/restart after the single run.
