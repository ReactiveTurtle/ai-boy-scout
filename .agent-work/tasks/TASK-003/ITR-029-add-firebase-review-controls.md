# ITR-029 - Add Firebase review controls

## Topic

Expose Firebase proposal review transitions in the remote note detail panel.

## Expected result

With local AI-read consent, a reviewer can request rejection review, retry/clarification, requeue processing, restore a rejected note, or reprocess a completed note with required comments.

## In scope

- add state-appropriate controls using the project-scoped Firebase review APIs;
- require AI-read consent for queue transitions and comments for every transition that requires one;
- update the selected remote proposal/note state and report the result after each action.

## Out of scope

- remote task list/status UI;
- merge or cross-page Firebase/SQLite note/task views;
- enabling Firebase project storage.

## Primary risk

Do not present AI queue actions as available when this installation has not granted consent for the bound local path.

## Verification

`npm --prefix web-ui run build` succeeded without budget warnings. `dotnet test BoyScout.sln` passed (276 tests). `git diff --check` passed.

## Result

The Firebase note detail panel now exposes rejection review, retry, clarification, processing requeue, restore, and reprocess. Queue transitions are disabled unless the selected local binding has AI-read consent and require a non-empty comment/reason. Successful human decisions update the selected remote note/proposal state. SQLite views remain unchanged.

## Residual risk

No live Firebase/browser session was available to exercise the new controls. Remote tasks are not yet listed in the main task workspace, and Firebase/SQLite data is not yet merged into a common feed.

## State

verifying; human acceptance pending
