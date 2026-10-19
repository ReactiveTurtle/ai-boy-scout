# ITR-030 - Browse and update Firebase tasks

## Topic

Complete remote task list operations needed after a Firebase proposal is confirmed.

## Expected result

Firebase tasks can be paged and a task can toggle between `todo` and `done` using project-root ETag/CAS while preserving source links and unrelated remote data.

## In scope

- retain/verify bounded project-scoped task paging;
- add direct task-ID status update in `FirebaseTaskRepository`;
- validate canonical task ID, project membership, status, source IDs;
- test valid status updates, invalid/missing/cross-project tasks, contention and preservation.

## Out of scope

- API/UI list/status controls;
- merged SQLite/Firebase task pagination;
- note/proposal state changes when task status changes.

## Primary risk

Task status writes must not overwrite other tasks or note/proposal updates in the shared project root.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests` passed (39 tests). `git diff --check` passed.

## Result

`FirebaseTaskRepository.UpdateTaskStatusAsync` validates a Firebase task ID and status, returns null for a missing task, validates project/source links, and changes status/time under project-root ETag/CAS. It mutates the existing task JSON node so unknown task fields and other project data survive.

## State

verifying; human acceptance pending
