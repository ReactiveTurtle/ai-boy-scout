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

- Hash a canonical serialization of every behavior-affecting stage field, excluding only the stage entity's own Id: AI instructions, actor/outcome policy, and conditional transitions with target-stage references.
- Keep stage ordering in the Process snapshot as an ordered list of `(StageId, StageHash)` references, so reordering does not rewrite unchanged stage snapshots.
- Use SHA-256 lowercase hexadecimal output and an immutable snapshot store keyed by that value.

## Human decision

The user chose to hash the full behavior definition except the stage entity's own Id. The ordered Process snapshot retains `(StageId, StageHash)` references.

## Out of scope

- Choosing a database schema or implementing the snapshot store.
- Defining AI response formats beyond the already agreed Title/Description updates and process outcomes.
