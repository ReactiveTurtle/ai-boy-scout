# ITR-019 - Persist Firebase worker outcomes

## Topic

Store each supported OpenCode outcome in the Firebase project root while atomically validating the current per-note processing lease.

## Expected result

A valid lease can save a task recommendation, a rejection recommendation, an unverified result, or a failure. Expired or stale leases cannot overwrite proposal data.

## Scope

- extend the Firebase proposal representation only with fields required by the current generator and reviewer;
- add lease-fenced result persistence operations to `FirebaseTaskRepository`;
- cover each outcome and stale-lease refusal with fake Firebase HTTP tests.

## Out of scope

- discovering and claiming the next queued note;
- wiring `ProposalGenerationService` or API endpoints to Firebase;
- changing local SQLite behavior or activating Firebase for any project;
- migrating proposals or tasks from SQLite.

## Risk and decision

Proposal and lease live under the same Firebase project-root ETag, so each result must validate the lease in the same conditional root write. Preserve unrelated notes, tasks, and Firebase fields.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests` passed (13 tests). `git diff --check` passed.

## Result

FirebaseTaskRepository now saves current/task, obsolete/rejection, unverified, and failed results only when the same-root proposal is in `processing` and the note lease is valid. Result writes use project-root ETag CAS and preserve unrelated data. Legacy `ready/current/create_task` proposals remain readable. Attempts remain unchanged on outcome writes; queue/retry transitions own attempt increments.

## State

verifying; human acceptance pending
