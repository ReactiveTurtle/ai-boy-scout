# ITR-004 - Expose the task review API

Status: verifying

## Expected result

The UI can browse raw and processed notes, inspect the current AI draft, confirm or retry it, and manage the two-state task list.

## In scope

- note lifecycle filter independent from legacy MCP statuses;
- current proposal read, confirm, and retry endpoints;
- task list and status endpoint;
- stable Russian-facing workflow semantics.

## Out of scope

- Angular implementation;
- MCP task tools;
- automatic confirmation.

## Primary risk

Exposing legacy report statuses directly would contradict the simpler note lifecycle and could hide active historical notes.

## Decision

Keep the compatibility report API unchanged and expose a separate note lifecycle where `open`, `accepted`, and `planned` mean `new`, while `resolved` and `dismissed` mean `processed`.

## Changed

- added paginated note browsing with `new` and `processed` lifecycle filters;
- added current proposal, confirmation, and retry endpoints;
- added task listing and two-state status updates;
- kept the existing report API unchanged.

## Verification

- Release build passed;
- all 16 Core tests passed;
- format verification passed.

## Human confirmation

- [ ] The note lifecycle mapping is accepted
- [ ] The proposal review API is accepted
