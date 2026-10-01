# ITR-016 - Return a proposal for clarification

Status: verifying

## Expected result

A human reviewer can return an agent result to the queue with a concrete clarification request, while other completed checks no longer stop the queue.

## In scope

- persist a clarification instruction on a proposal;
- return ready, unverified, or failed proposals to `queued` with that instruction;
- include the instruction in the next OpenCode review prompt;
- expose the backend clarification action through the note API;
- let only `processing` block the next agent run.

## Out of scope

- free-form multi-message conversations;
- editing a confirmed task;
- changing agent permissions.
- changing the UI;

## Primary risk

The agent could ignore the human clarification or stale instructions could leak into unrelated review intents.

## Decision

One latest clarification instruction is stored per proposal. A rejection review clears it; a clarification request clears rejection intent and generated output before queueing.

## Verification

- Schema v7 -> v8 migration preserves proposals with `requested_clarification = NULL` and
  database constraints reject blank clarification and mixed rejection/clarification requests.
- `RequestClarification` tests cover trimmed and bounded input, missing reports/proposals,
  active report state, project consent, allowed proposal states, output clearing, attempt count,
  persisted queue claim, and queued/processing/terminal conflicts.
- Prompt verification covers JSON-escaped untrusted clarification data, the requirement to
  address the exact clarification, independent source inspection, and an independent
  `verificationSummary` argument.
- Regression tests confirm rejection review and restore clear clarification, untouched
  proposals start with `NULL`, `processing` blocks a claim, and `ready` does not.
- `dotnet test BoyScout.sln --configuration Release`: 72 passed.
- `npm test`: 72 passed; solution build completed with 0 warnings and 0 errors; MCP smoke passed.
- `dotnet format BoyScout.sln --no-restore --verify-no-changes`: passed.
