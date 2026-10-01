# ITR-014 - Add explicit note review states

Status: verifying

## Expected result

Every note reports whether it is queued, being checked by OpenCode, awaiting a user decision, processed, or rejected; rejected notes can be restored to the queue.

## In scope

- add a persisted `queued` proposal state;
- claim queued proposals before untouched reports;
- expose a derived review status for each note;
- restore a rejected note as a normal queued review;
- preserve deterministic single-agent processing.

## Out of scope

- rejection form UI;
- redesigning project consent;
- parallel OpenCode processes.

## Primary risk

Adding `queued` can break existing assumptions that every nonterminal proposal is already processing or awaiting a decision.

## Decision

The proposal state models queue ownership explicitly. The user-facing note state is derived as `queued`, `checking`, `awaiting_decision`, `processed`, or `rejected`.

## Verification

- migration preserves existing proposals;
- queued proposals are claimed before untouched reports;
- only one processing proposal is selected;
- ready, unverified, and failed map to awaiting decision;
- restore changes a dismissed report and rejected proposal to open/queued.

## Evidence

- visible notes are batch-loaded through `/api/notes/workflows` after initial load, pagination, refresh, and every four-second poll;
- list rows and selected-note metadata render the exact five-state labels from `reviewState`, with report status used only as a fallback;
- selected workflow and proposal are loaded independently from the global activity proposal and drive all note-level decisions;
- confirm, retry, rejection review, and restore refresh the selected review, visible workflow map, notes, projects, and counts without dropping the selected note when it remains visible;
- `npm run build` completed successfully for `web-ui` (with a component-style budget warning: 19.98 kB against 18.00 kB).
