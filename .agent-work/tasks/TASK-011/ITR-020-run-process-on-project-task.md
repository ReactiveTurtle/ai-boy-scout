---
id: ITR-020
topic: Start and advance one ProcessRun on a ProjectTask
type: implementation
state: verifying
---

## Expected result

A ProjectTask can start exactly one ProcessRun from a selected Process snapshot and advance it sequentially through stages/blocks, including bounded stage-back-links.

## Scenarios

1. Starting captures the Process snapshot and points the run at the first block/stage; a second start is rejected even after the run ends.
2. A completed stage must update Title or Description; supplied fields are validated atomically and omitted fields remain unchanged.
3. The run advances in stage order, then block order, and completes after the final stage.
4. A backward link is traversed at most its configured repeat count; the next attempt marks the run Blocked with an error.
5. Invalid/no-op stage results do not mutate the ProjectTask or run position.
6. Completing a stage after the run is Completed/Blocked is rejected without mutation.

## In scope

- Add ProcessRun state owned by ProjectTask, initialized from `Process.CaptureSnapshot()`.
- Add deterministic current-stage advancement and per-link repeat counters.
- Apply stage Title/Description results through ProjectTask domain invariants.
- Enforce one ProcessRun per ProjectTask.

## Out of scope

- Waiting-for-user prompt/response and resuming the same stage (ITR-021).
- Persisting runtime state or adding schema/migrations, Application/API endpoints, SignalR, and UI.
- Parallel branches, predicates, or cross-ProcessBlock backward links.

## User decisions

- A ProjectTask has one ProcessRun total.
- A stage must change Title or Description to complete; a user-wait result may pause without either change.
- Reaching a backward-link repeat limit blocks the run with an error.

## Primary risk

Task updates and run progression must be atomic when a stage result is invalid; a loop-limit error must leave a clear blocked state.

## Verification

- Build the TodoGraph solution.
- Run an isolated domain smoke for start, ordered progression, field updates, bounded loops, and rejected/atomic outcomes.

## Verification result

- `ProjectTask.StartProcess` captures an immutable snapshot and permits only one run; `ProcessRun` advances through ordered stages/blocks and counts backward-link traversals per source cycle.
- Valid stage outputs update Title/Description atomically. A no-op or invalid output leaves both the task and run position unchanged. Reaching the configured link limit applies the valid stage output, then marks the run Blocked with an error.
- Release solution build passed with 0 warnings and 0 errors.
- A temporary domain runtime smoke passed for start, sequential stage/block progression, terminal completion, one-run-only behavior, atomic result validation, bounded looping, and blocked-state behavior; its source was removed.
- Operational API check started the freshly built host on temporary port 5231 with an isolated database; health, project creation, and ProjectTask create/list returned successfully. The host was stopped and the port released.
- ProcessRun state is not persisted or exposed through the API; waiting-for-user pause/resume remains ITR-021.
