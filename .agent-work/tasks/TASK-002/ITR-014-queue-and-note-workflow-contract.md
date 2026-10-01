# ITR-014 - Separate queued requests from agent processing

Status: verifying

## Expected result

User requests remain visibly queued until the agent atomically claims them, while note-level APIs expose deterministic workflow and proposal state.

## In scope

- schema v7 and the `queued` proposal state;
- atomic queued-first claiming with consent and active blockers;
- queued retry and rejection-review requests;
- per-note proposal, batch workflow, and rejected-note restore APIs;
- exact user-facing review-state derivation;
- Core, migration, release, and MCP smoke verification.

## Out of scope

- Angular UI changes;
- runtime database migration;
- changes to external projects or the four MCP tools.

## Primary risk

A queued request could bypass consent, overtake an older queued request, or be mistaken for active agent processing.

## Decision

Retry attempts increment when the user queues the retry. Claiming and restoring preserve the attempt count. Consented `processing` and `ready` proposals are global blockers; queued, unverified, failed, and unconsented proposals are not.

## Verification

- schema v6 data survives migration to v7;
- queued claims are ordered, consent-aware, and blocked only by consented processing or ready proposals;
- retry and rejection review return queued proposals;
- workflow derivation covers queued, checking, awaiting-decision, processed, and rejected states;
- restore is atomic and invalid states fail;
- batch workflows preserve input order and omit missing reports;
- per-report proposal lookup distinguishes no proposal from a missing report.

## Evidence

- `dotnet test BoyScout.sln --configuration Release`: 54 tests passed;
- `npm test`: 54 tests passed, solution build completed with 0 warnings and 0 errors, MCP smoke passed against schema v7;
- `dotnet format BoyScout.sln` completed successfully;
- `git diff --check` reported no whitespace errors;
- migration coverage preserves a populated ready proposal from schema v6;
- state-machine coverage verifies queued ordering, consent, processing/ready blockers, retries, rejection requests, deterministic current selection, restore, batch order, and all review-state derivations.

## Intentionally unchanged

- Angular UI;
- `%LOCALAPPDATA%` runtime database;
- external projects and MCP tool contract.
