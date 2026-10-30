---
id: ITR-014
topic: Model global Process definitions and ordered stages
type: implementation
state: verifying
---

## Expected result

TodoGraph.Domain exposes a global `Process` aggregate whose stages have stable IDs, required AI instructions, and explicit list order; editing order or instructions preserves stage identity.

## Scenarios

1. A Process name and each stage's instructions are trimmed and must be non-empty.
2. Adding stages appends them to the ordered stage list.
3. Updating one stage changes its instructions without replacing its identity or changing its position.
4. Reordering with a complete permutation changes order while retaining stage IDs and instructions.
5. Reordering with a missing, duplicate, or foreign stage ID is rejected without changing the current order.
6. Updating or removing a stage not owned by the Process is rejected.

## In scope

- Add the global `Process` aggregate and its owned `ProcessStage` entities.
- Add rename, add, update-instructions, remove, and reorder operations.
- Keep stage ordering in the aggregate's ordered collection.

## Out of scope

- Conditional-transition representation or evaluation.
- Waiting-for-user-input behavior.
- Starting a Process on a ProjectTask, runtime state, and task updates from stage results.
- Process/stage snapshots, canonical serialization, SHA-256, persistence, API, and UI.

## Primary risk

Stage identity and ordering must remain stable across edits, while invalid reorder requests must not partially mutate the aggregate.

## Verification

- Compile the TodoGraph solution.
- Run an isolated domain smoke scenario for add, update, reorder, and rejected reorder/remove requests.

## Decision boundary

The accepted decisions establish a global named Process with an ordered list of stages and AI instructions. Branch/wait and snapshot/hash behavior remain separate iterations because their concrete data and outcome formats are not yet defined.

## Verification result

- Added `Process` with a required trimmed name and ordered `ProcessStage` entities with required trimmed instructions. The aggregate supports rename, append, update, remove, and exact-permutation reorder operations.
- `dotnet build BoyScout.sln --configuration Release --no-restore -m:1 -p:OutDir=<approved temp output>` passed with 0 warnings and 0 errors.
- A temporary runtime smoke verified append order, stable stage identity through edits/reordering, rejection of invalid reorder/update/remove requests, and no partial mutation after rejected operations. It passed; the temporary harness source was removed.
- Conditional transitions, user waits, process snapshots/hash, persistence, endpoints, and UI were not implemented or tested in this iteration.

## Topic evolution

- Initial model: Process directly owned an ordered stage list.
- User clarification: a Process is composed of ProcessBlocks; each block contains stages and blocks connect within the Process.
- The direct Process-to-stage ownership in this iteration is superseded by the ProcessBlock model; preserve stage identity/instruction behavior while refactoring in ITR-018.
