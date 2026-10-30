---
id: ITR-018
topic: Refactor Process into a graph of ProcessBlocks
type: implementation
state: verifying
---

## Expected result

TodoGraph.Domain models a Process as a sequence of ProcessBlocks; each ProcessBlock owns ordered stages, and a stage may have a bounded backward link. Execution has no branches or parallelism.

## Scenarios

1. A Process definition contains at least one ProcessBlock.
2. A ProcessBlock contains at least one ordered stage and preserves stage identities through edits.
3. A Process contains at least one ProcessBlock; removing the last block or last stage in a block is rejected.
4. A stage backward link targets an earlier stage in the same block and stores its per-cycle repeat limit.
5. The model has no branching or parallelism; the stage definition records a per-cycle repeat limit, whose runtime enforcement is a later iteration.
6. Existing ProjectTask ownership and its API/persistence behavior remain unchanged.

## In scope

- Replace direct `Process.Stages` ownership with sequential ProcessBlocks containing sequential stages.
- Preserve stage instruction validation and identity/order behavior.
- Enforce the user-confirmed minimum-one-block/minimum-one-stage invariants.
- Model one backward link per source stage, targeting only an earlier stage in the same block, with a positive per-cycle repeat limit.

## Out of scope

- Branching predicates, parallel execution, and persistent stage-link runtime behavior (ITR-016/017).
- Waiting execution, task runtime, snapshots/hash, persistence, API, and UI.

## Confirmed design

- The user confirmed in ITR-016 that backward links are stage-to-earlier-stage within a ProcessBlock, counted per cycle, and exceeding the limit blocks the run with an error.

## Verification result

- Refactored `Process` to own an ordered non-empty list of ProcessBlocks; each block owns an ordered non-empty list of ProcessStages.
- Added validated stage backward links targeting earlier stages in the same block, with a positive repeat limit; duplicate links and reorders that invalidate a link are rejected without mutation.
- Release solution build passed with 0 warnings and 0 errors.
- A temporary runtime domain smoke passed for minimum block/stage counts, order and identity preservation, valid/invalid backward links, per-cycle limit storage, and safe removal; its source was removed.
- Runtime loop counting/error persistence, snapshot/hash storage, and serialization are not part of this iteration.
