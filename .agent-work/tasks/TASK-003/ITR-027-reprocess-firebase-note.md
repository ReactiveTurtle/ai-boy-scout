# ITR-027 - Reprocess Firebase note

## Topic

Reopen a resolved Firebase note for a fresh worker pass, safely removing only its exclusive linked task.

## Expected result

A resolved note with no proposal or a confirmed proposal is reset to open/queued with a required comment. If one task is linked exclusively to that note, it is deleted in the same project-root CAS; shared or multiple linked tasks refuse the operation.

## In scope

- add `ReprocessCompletedNoteAsync` to FirebaseTaskRepository;
- require resolved note and absent/confirmed proposal;
- detect linked tasks from SourceReportIds and delete only one exclusive task;
- reset proposal/note state atomically, clear previous result and reviewer timestamps, preserve attempt count/context as SQLite does;
- test no task, exclusive task, multiple/shared task refusal, wrong states, comment validation and CAS retry.

## Out of scope

- API/UI endpoint or task list/status browsing;
- migration or Firebase activation.

## Primary risk

Do not delete a Firebase task that has sources beyond the note being reprocessed. Task deletion, note reopening and proposal reset must be one root CAS.

## Verification

- `dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests` passed (37 tests).
- `git diff --check`.
- Fake HTTP coverage verifies no-task creation, exclusive deletion, shared/multiple-link refusal, state/comment validation, result reset and attempt preservation, lease fencing, CAS retry, malformed data refusal, and preservation of unrelated data.

## Result

- Added `ReprocessCompletedNoteAsync`, returning the updated note, proposal, and nullable deleted task ID.
- Task deletion, note reopening, proposal reset, and lease invalidation are performed in one project-root ETag/CAS write.
- Only ETag conflicts retry; each retry validates the latest tasks and proposal before writing.

## State

verifying
