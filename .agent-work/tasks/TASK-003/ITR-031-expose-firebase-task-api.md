# ITR-031 - Expose Firebase task API

## Topic

Expose project-scoped Firebase task paging and status updates through the localhost API.

## Expected result

The UI can fetch remote task pages and toggle task status only for a Firebase project bound on this installation.

## In scope

- add GET remote tasks page endpoint using the existing Firebase key cursor;
- add PATCH remote task status endpoint using the repository ETag/CAS operation;
- validate bound project and remote task/project identity;
- map missing/incompatible task, invalid status, credential, and Firebase transport errors.

## Out of scope

- Angular task panel;
- mixed Firebase/SQLite tasks list or totals;
- changing local SQLite task routes.

## Primary risk

Remote task IDs and remote project IDs are not SQLite IDs; all operations must remain in the bound Firebase project.

## Verification

`dotnet test BoyScout.sln` passed (278 tests). `git diff --check` passed. Firebase repository task browse/status tests cover remote state behavior; minimal API routes are compile-verified but do not yet have endpoint integration tests.

## Result

Added bound-project endpoints for remote task key paging and status updates. Task IDs are checked against the route's Firebase project ID; task state changes use the repository's atomic project-root ETag/CAS operation. Local SQLite task endpoints remain unchanged.

## State

verifying; human acceptance pending
