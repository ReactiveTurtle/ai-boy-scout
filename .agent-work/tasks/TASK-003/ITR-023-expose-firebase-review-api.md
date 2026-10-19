# ITR-023 - Expose Firebase review APIs

## Topic

Expose remote note data and proposal decisions for locally bound Firebase projects through the localhost API.

## Expected result

The API can page remote notes, read one note/proposal, and atomically confirm a current task proposal or obsolete rejection. Requests are scoped to a Firebase project bound on this installation.

## In scope

- add project-scoped remote note list/detail and proposal GET endpoints;
- add project-scoped positive confirmation and rejection confirmation endpoints;
- preserve localhost-only credentials boundary and map missing binding/remote data/errors to explicit statuses.

## Out of scope

- Angular presentation or mixed SQLite/Firebase list pagination;
- retry, clarification, restore, reprocess transitions;
- enabling Firebase for a project or new-note routing.

## Primary risk

Firebase service-account credentials must remain server-side, and remote project IDs must not be confused with local SQLite project IDs.

## Verification

`dotnet test BoyScout.sln` passed (264 tests). `git diff --check` passed. Firebase repository state transitions are covered by focused tests; the new minimal API routes were compile-verified but do not yet have endpoint integration tests.

## Result

Added bound-project endpoints to page remote notes, read a remote proposal, confirm a current proposal into a remote task, and confirm an obsolete rejection. Credentials stay server-side. All routes require a local binding; note-specific routes verify that the note ID belongs to the selected remote project before reading or changing it. Project IDs remain remote IDs and are not treated as SQLite IDs.

## State

verifying; human acceptance pending
