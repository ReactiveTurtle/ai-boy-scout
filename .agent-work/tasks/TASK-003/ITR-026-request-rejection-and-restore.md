# ITR-026 - Rejection review, requeue, and restore

## Topic

Add the remaining Firebase transitions that place an active note into the queue or restore a rejected note.

## Expected result

An installation can request a rejection review, return a processing proposal to the queue with a comment, or restore a rejected Firebase note with a comment. Proposal/note state and lease fencing are committed atomically.

## In scope

- request rejection review for notes without a proposal or with ready/unverified/failed proposals;
- requeue a processing proposal with a required comment;
- restore a dismissed note with an optional rejected proposal and required comment;
- invalidate processing lease in the same root CAS when canceling an active worker;
- test state eligibility, comments, attempts, atomic note/proposal updates, lease fencing and conflicts.

## Out of scope

- reprocessing resolved notes or deleting linked remote tasks;
- API/UI wiring;
- Firebase task status/list integration or storage activation.

## Primary risk

Queue transitions must preserve human-request context while preventing a processing worker from saving after cancellation.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests` passed: 32 passed, 0 failed.

`git diff --check` passed.

## Result

- Added atomic Firebase operations for requesting rejection review, requeueing a processing proposal, and restoring a dismissed note.
- Processing requeue and restore invalidate the report lease in the same root CAS. Existing proposal metadata and unrelated root data are preserved.
- Fake-HTTP tests cover new/existing proposal paths, eligibility and comment validation, attempts/context, note/proposal atomic state and timestamps, lease fencing, and CAS retries.
- Changed only `src/BoyScout.Core/Storage/Firebase/FirebaseTaskRepository.cs`, `tests/BoyScout.Core.Tests/FirebaseTaskRepositoryTests.cs`, and this iteration record.

## State

verifying
