# ITR-012 - Skip notes without project access

Status: verifying

## Expected result

A dormant or manually actionable proposal does not block processing the next permitted note.

## In scope

- block claims only while a consented proposal is `processing` or `ready`;
- preserve consented `unverified` and `failed` proposals for manual retry without blocking the queue;
- hide dormant unconsented proposals from the current worker/UI slot;
- process multiple proposals safely if consent is restored while another item is active;
- preserve dormant proposal state for later resumption.

## Primary risk

Restoring consent could expose multiple active proposals and break code that assumed exactly one row.

## Decision

Access controls queue eligibility, not persisted workflow state. Only consented `processing` and
`ready` proposals serialize automatic work. Current and processing queries select one eligible
proposal deterministically instead of asserting global uniqueness, while current selection keeps
`processing`, `ready`, `unverified`, `failed` priority.

## Verification

- an unconsented active proposal does not block a permitted report;
- consented `unverified` and `failed` proposals do not block the next permitted report;
- consented `processing` and `ready` proposals block another claim;
- an unconsented processing proposal remains unchanged;
- restoring consent makes the proposal eligible again;
- multiple eligible proposals are returned one at a time without `SingleOrDefault` failures.

## Verification evidence

- `dotnet test BoyScout.sln --configuration Release --no-restore`: 30 tests passed;
- `dotnet format BoyScout.sln --verify-no-changes --no-restore`: passed;
- tests cover consented `unverified` and `failed` queue progress, `processing` and `ready`
  blockers, unchanged revoked `processing`, regrant with two eligible processing proposals,
  and the full current-state priority order.
