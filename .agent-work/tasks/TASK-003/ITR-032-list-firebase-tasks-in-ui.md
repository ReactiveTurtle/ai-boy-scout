# ITR-032 - List Firebase tasks in UI

## Topic

Show bound Firebase project tasks and status controls in project settings.

## Expected result

The user can page remote tasks created by Firebase proposals and toggle a task between todo/done without modifying SQLite tasks.

## In scope

- add remote task page loading and cursor state to the Firebase project panel;
- show title, status, source note IDs and confirmer attribution;
- call the bound-project API to update task status and update the visible row;
- refresh the task panel after confirming a Firebase proposal.

## Out of scope

- merging Firebase and SQLite tasks in the main Tasks workspace;
- changing local task behavior or Firebase storage activation.

## Primary risk

Do not pass remote project IDs/task IDs to SQLite task endpoints or show a remote task as a local task.

## Verification

`npm --prefix web-ui run build` succeeded without budget warnings. `dotnet test BoyScout.sln` passed (278 tests). `git diff --check` passed.

## Result

The project settings panel can load Firebase task pages, show source-note IDs/confirmer attribution, and toggle each remote task between todo/done through the bound-project API. Confirming a Firebase proposal refreshes the task panel. SQLite task lists and operations are unchanged.

## Residual risk

No live Firebase/browser test was run. Remote tasks remain in a project settings panel rather than the main SQLite task workspace; merged pagination is still required before project activation.

## State

verifying; human acceptance pending
