---
id: ITR-012
topic: Define the content-addressed stage snapshot key
type: decision
state: accepted
---

## Expected result

Define which immutable stage data contributes to a stage snapshot hash so Process snapshots reuse unchanged stage snapshots without missing behavior changes.

## Known rules

- Process definitions are global and ordered.
- A ProjectTask explicitly starts a selected Process and captures a snapshot.
- Stage instructions drive AI behavior; transitions may branch or wait for user input.
- Editing a definition must not change an already-started task's snapshot.

## Decision

- Hash a canonical serialization of every behavior-affecting stage field, excluding only the stage entity's own Id. Currently this includes AI instructions and stage-link behavior.
- Keep stage ordering in a ProcessBlock snapshot as an ordered list of `(StageId, StageHash)` references; keep ProcessBlock ordering in the Process snapshot, so reordering does not rewrite unchanged stage snapshots.
- Use SHA-256 lowercase hexadecimal output and an immutable snapshot store keyed by that value.

## Human decision

The user chose to hash the full behavior definition except the stage entity's own Id. The ordered Process snapshot retains `(StageId, StageHash)` references.

## Follow-up decision — 2026-10-30

- A ProjectTask starting a Process captures the full graph of ProcessBlocks in that Process.
- A stage backward link's target StageId and per-cycle repeat limit affect behavior and therefore contribute to the owning stage's canonical hash.

## Out of scope

- Choosing a database schema or implementing the snapshot store.
- Defining AI response formats beyond the already agreed Title/Description updates and process outcomes.
