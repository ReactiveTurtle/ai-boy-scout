# ITR-024 - Review Firebase notes in UI

## Topic

Make bound Firebase notes and their worker proposals reviewable from the project settings panel.

## Expected result

The user can page a remote project's notes, inspect one note/proposal, confirm a current proposal as a Firebase task, or confirm an obsolete recommendation as rejected. SQLite history and behavior remain unchanged.

## In scope

- add a Firebase note list and selected-note proposal detail to project settings;
- use the project-scoped API and existing remote binding/project setup;
- expose current-task and obsolete-rejection confirmation actions;
- update list/detail state after a human decision and show a clear result.

## Out of scope

- mixing/paging Firebase and SQLite records in the main note/task workspaces;
- Firebase retry/clarification/restore/reprocess actions;
- listing remote tasks in the main task workspace;
- enabling Firebase writes for any project.

## Primary risk

Remote Firebase project IDs differ from local SQLite project IDs. All UI requests must use the remote project ID from its binding, while the current SQLite panel remains unchanged.

## Verification

`npm --prefix web-ui run build` succeeded with no budgets exceeded. `dotnet test BoyScout.sln` passed (264 tests). `git diff --check` passed.

## Result

The existing project settings page now loads the bound Firebase project's note pages, opens the selected note's worker proposal, and exposes the two human outcomes: create a remote task or confirm rejection. Successful decisions update the visible remote note/proposal state. The existing SQLite note feed and its behavior are unchanged.

## Residual risk

No live Firebase account or browser session was available to exercise the new panel. Retry/clarification/restore/reprocess and remote task listing are not yet connected to the UI.

## State

verifying; human acceptance pending
