# ITR-036 - Firebase activation wizard UI

## Topic

Present preflight and explicit project activation in the bound Firebase project's settings panel.

## Expected result

The user can review local/remote counts, consent and worker readiness, then explicitly enable Firebase for new notes using the fresh preflight token. No action runs a migration or moves SQLite history.

## In scope

- show a multi-step/checklist-style preflight in project settings;
- show the local snapshot checksum, Firebase/local note/task counts and blockers;
- require a second explicit confirm after a ready preflight;
- update enabled state after confirmation and keep old SQLite records described as untouched.

## Out of scope

- disabling an enabled project's Firebase writes;
- merging the main SQLite note/task feed with the Firebase panels;
- live Firebase browser exercise.

## Primary risk

Never expose the confirm button before a ready preview or reuse the token after binding/consent changes.

## Verification

`npm --prefix web-ui run build` succeeded without budget warnings. `dotnet test BoyScout.sln` passed (291 tests). `git diff --check` passed.

## Result

The bound Firebase project panel now has two explicit stages: a preflight call and a separate confirm button. The preflight displays remote/local note/task counts, path, AI consent, worker state, and the local SQLite snapshot checksum; the confirmation uses the preflight token. No data is migrated or deleted, and enabled state is displayed after confirmation.

## Residual risk

No live Firebase account or browser session was available to exercise the preflight and confirm routes. The write-access probe is verified against a fake HTTP handler only.

## State

verifying; human acceptance pending
