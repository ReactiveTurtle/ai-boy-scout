# ITR-025 - Retry or clarify Firebase proposal

## Topic

Return an active Firebase proposal in `ready`, `unverified`, or `failed` state to the worker queue with a required reviewer comment.

## Expected result

The retry or clarification comment and cleared previous agent result are persisted atomically as `queued`; the next worker claim can process the note again without being fenced by an old lease.

## In scope

- add Firebase retry and clarification transitions matching SQLite validation/state behavior;
- require and trim a non-empty comment (maximum 2,000 characters);
- increment attempts, clear generated fields/timestamps, preserve CreatedAt and unrelated data;
- clear/invalidate a prior processing lease in the same project-root CAS;
- test allowed/disallowed states, comments, lease invalidation, conflicts and preservation.

## Out of scope

- rejection-review request, processing requeue, restore or reprocess transitions;
- API/UI wiring;
- project activation or storage routing.

## Primary risk

An earlier worker must not save a stale result after the human queues a retry. Proposal reset and lease generation invalidation must occur in the same project-root CAS.

## Verification

Run `dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests`.

## Result

- Added `RetryProposalAsync` and `RequestClarificationAsync` with trimmed required comments capped at 2,000 characters.
- Both transitions accept only active notes with ready, unverified, or failed proposals; they clear generated result fields and timestamps, increment attempts, and queue the proposal in a root ETag CAS.
- Retry retains review intent and requested rejection reason. Clarification sets proposal intent and clears the requested rejection reason.
- Per-note leases are invalidated in that same CAS using the existing lease schema and Firebase server time, fencing stale workers.
- Tests cover transition values, disallowed states and comments, lease fencing, ETag conflict retry, and preservation of unrelated fields.

## Verification

- `dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests` - passed (28 tests).
- `git diff --check` - passed.

## State

verifying
