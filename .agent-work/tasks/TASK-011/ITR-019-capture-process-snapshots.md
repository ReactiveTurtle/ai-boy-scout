---
id: ITR-019
topic: Capture immutable Process snapshots with stage hashes
type: implementation
state: verifying
---

## Expected result

Capturing a Process produces an immutable ordered snapshot of its blocks and stages, with SHA-256 stage hashes that exclude only the stage's own ID.

## Scenarios

1. Identical stage behavior has the same lowercase SHA-256 hash even when the Stage IDs differ.
2. Changing instructions, backward-link target, or per-cycle repeat limit changes that stage hash.
3. ProcessBlock snapshots preserve stage order as `(StageId, StageHash)` references; Process snapshots preserve block order.
4. Reordering blocks/stages changes reference order but does not change unchanged stage hashes.
5. Repeated capture is deterministic, and later definition edits do not mutate an already captured snapshot.
6. The Process snapshot contains each distinct content-addressed stage snapshot once, even if multiple stage refs share a hash.

## In scope

- Add immutable stage/block/Process snapshot types in TodoGraph.Domain.
- Canonically serialize current behavior fields and compute lowercase SHA-256 stage hashes.
- Capture the full ProcessBlock graph and its ordered stage references from `Process`.

## Out of scope

- Persistent snapshot storage, schema/migrations, API, or UI.
- ProjectTask Process selection/start, runtime execution, wait/resume behavior, and repeat counters/errors.
- A separate Process-level content hash; only stage snapshots are content-addressed in this iteration.

## Primary risk

Canonical stage serialization must be stable and exclude only the stage's own ID while retaining the backward-link target and repeat limit.

## Verification

- Build the TodoGraph solution.
- Run an isolated domain smoke for hash stability/change sensitivity, ordering, deduplication, and snapshot immutability.

## Verification result

- Added `Process.CaptureSnapshot()` for ordered ProcessBlocks and stage references.
- Stage content is serialized with stable property order and hashed using lowercase SHA-256. The payload includes instructions and optional backward-link target/repeat limit, but not the stage's own ID.
- Snapshot lists are defensive read-only copies; identical stage hashes are included once and referenced by each StageId.
- Release solution build passed with 0 warnings and 0 errors.
- A temporary runtime smoke passed for hash independence from StageId, sensitivity to instructions/target/limit changes, ordered references, deduplication, and immutability after edits. The temporary harness source was removed.
- Snapshot persistence and attaching snapshots to a ProjectTask run were not implemented or verified.
