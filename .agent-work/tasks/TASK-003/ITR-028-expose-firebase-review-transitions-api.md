# ITR-028 - Expose Firebase review transitions

## Topic

Expose retry, clarification, rejection-review request, processing requeue, restore, and reprocess through project-scoped API routes.

## Expected result

The local review UI can invoke each repository transition for a bound Firebase project without confusing remote IDs with local SQLite IDs or bypassing AI-read consent for queued work.

## In scope

- add project-scoped POST routes for each Firebase review transition;
- validate remote note IDs belong to the route project and the project is bound locally;
- require local AI-read consent for actions that enqueue worker processing;
- map validation/state/missing/remote errors to explicit HTTP responses.

## Out of scope

- Angular controls for these routes;
- Firebase task browse/status or mixed SQLite/remote pagination;
- changing storage activation or note creation routing.

## Primary risk

Never use a remote project ID as a SQLite project ID or let a note ID from another remote project pass a bound project route.

## Verification

`dotnet test BoyScout.sln` passed (276 tests). `git diff --check` passed. Repository transition tests cover state constraints and atomic lease invalidation; minimal API routes are compile-verified but do not yet have endpoint integration tests.

## Result

Added bound-project routes for rejection-review request, clarification, retry, processing requeue, restore and reprocess. Every route validates that the remote note ID belongs to the remote project bound on this installation; actions that queue AI work additionally require that binding's AI-read consent. Request comments are validated by the repository and credentials stay server-side.

## State

verifying; human acceptance pending
