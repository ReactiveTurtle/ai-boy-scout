# ITR-020 - Claim consented Firebase notes

## Topic

Discover and claim one eligible Firebase note for worker processing.

## Expected result

A note is returned with a valid per-note lease only when its remote project binding has AI-read consent, the note belongs to the bound local path, the note is active, and it has no blocking proposal/task. Claiming writes a `processing` proposal atomically under the same project-root ETag; an expired processing lease can be recovered safely.

## In scope

- scan eligible notes in one bound remote project in deterministic oldest-first order;
- verify binding consent and the source path before claiming;
- acquire/recover the note lease and create or transition the proposal to `processing` under same-root CAS;
- test competition, expired lease recovery, duplicate prevention, path/consent filtering, and CAS conflicts.

## Out of scope

- invoking OpenCode or renewing a lease during generation;
- API/MCP store routing, mixed SQLite/Firebase pagination, or human decisions;
- enabling Firebase project storage.

## Primary risk

A lease may be acquired while a human or another process changes proposal state. The claim must re-read the root and validate the lease, note, proposal, and task state in one CAS transition; a stale/ready proposal must never be reset to processing.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj` passed (258 tests). `git diff --check` passed.

## Result

`FirebaseTaskRepository.ClaimNextEligibleNoteAsync` scans one remote project oldest-first, excludes notes outside the consented local path or with inactive status/task/non-queued proposal, acquires a per-note lease, then re-reads and writes the `processing` proposal with project-root ETag/CAS. It can take over an expired `processing` lease, preserves queued review context and attempts, and starts a new proposal at attempt 1. It does not invoke OpenCode or force-release a lease after a failed claim.

## State

verifying; human acceptance pending
