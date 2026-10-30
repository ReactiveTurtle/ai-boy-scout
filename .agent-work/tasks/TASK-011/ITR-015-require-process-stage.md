---
id: ITR-015
topic: Require a stage in every Process definition
type: implementation
state: verifying
---

## Expected result

Every TodoGraph `Process` always has at least one stage, including after edits.

## Scenarios

1. Creating a Process requires a non-blank name and initial stage instructions and produces exactly one stage.
2. Additional stages append in order; existing rename, edit, and reorder behavior remains intact.
3. Removing a stage from a multi-stage Process succeeds and preserves the others.
4. Removing the only stage is rejected and leaves the Process unchanged.
5. Blank initial instructions or a blank Process name are rejected.

## In scope

- Require initial stage instructions when constructing a Process.
- Prevent removal of the final stage.
- Verify that rejected removal preserves the aggregate.

## Out of scope

- Process-to-Process links and their execution/snapshot meaning (tracked in ITR-016).
- Conditional-transition predicates and their data contract.
- Waiting-for-user runtime state, ProcessTask execution, snapshots, persistence, API, and UI.

## Verification

- Build the TodoGraph solution.
- Run an isolated domain smoke for creation and the final-stage invariant.

## User decision

The user specified that a Process must not be empty. The catalog may construct it with its initial stage; domain edits must not remove that last stage.

## Verification result

- `Process.Create` now requires the first stage instructions; a Process cannot be constructed without a stage.
- Removing the final stage throws `InvalidOperationException` without changing the aggregate.
- Release solution build passed with 0 warnings and 0 errors.
- A temporary runtime smoke passed for required initial stage data, stage editing/reordering, successful removal when multiple stages exist, and rejection/no mutation when removing the final stage. The temporary harness source was removed.
