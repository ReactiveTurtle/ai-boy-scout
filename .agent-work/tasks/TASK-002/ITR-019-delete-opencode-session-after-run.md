# ITR-019 - Delete the OpenCode session after a run

Status: verifying

## Expected result

Every OpenCode session created by a Boy Scout verification run is deleted after success, failure, timeout, or cancellation.

## In scope

- extract the created session ID from JSONL events;
- delete exactly that session through `opencode session delete`;
- perform cleanup on every process exit path;
- preserve the primary generation error if cleanup also fails;
- test session ownership and cleanup behavior without deleting user sessions.

## Out of scope

- deleting historical sessions created before this change;
- deleting sessions not created by Boy Scout;
- changing OpenCode global storage.

## Primary risk

An incorrect session ID could delete an unrelated user session.

## Decision

Cleanup uses only the session ID emitted by the exact `opencode run` process. No session-list heuristics or title matching are allowed.

The process runner returns stdout after normal exit or process-tree termination so the generator can extract the owned session before handling the primary result. Cleanup reuses the configured executable, `OPENCODE_CONFIG`, and run working directory with an independent 15-second timeout and `CancellationToken.None`.

## Verified facts

- installed OpenCode version: `1.18.29`;
- the tagged `run.ts` JSON formatter writes top-level `sessionID` into every emitted event;
- an exported existing OpenCode session also stores `sessionID` on message and part records;
- `opencode session list --format json` was used only to inspect existing metadata; no model run was started and no real session was deleted.

## Changed

- `OpenCodeProposalGenerator` strictly extracts one consistent top-level `sessionID` or `sessionId` and owns cleanup/error precedence;
- `OpenCodeCommandRunner` centralizes redirected process execution, bounded stderr, timeout, process-tree kill, and stream completion;
- dependency injection registers the runner;
- focused tests use a fake runner and never invoke the real OpenCode executable;
- README documents the session lifecycle and no-fallback rule.

## Verification

- successful run deletes its emitted session;
- nonzero exit, timeout, and cancellation attempt the same cleanup;
- missing session ID never triggers broad deletion;
- cleanup failure cannot hide the original processing failure.

Evidence:

- focused `OpenCodeProposalGeneratorTests`: 12 passed;
- `dotnet test BoyScout.sln --configuration Release`: 95 passed;
- `npm test`: 95 tests passed, solution build passed with zero warnings, MCP smoke test passed;
- `dotnet format BoyScout.sln --verify-no-changes`: passed;
- no real OpenCode model run was started and no real OpenCode session was deleted during implementation or tests.
