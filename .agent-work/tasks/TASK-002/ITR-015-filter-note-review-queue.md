# ITR-015 - Filter the note review queue

Status: verifying

## Expected result

The user can open all notes awaiting a decision instead of finding proposals one at a time in the chronological list.

## In scope

- filter notes by queued, checking, awaiting decision, processed, and rejected;
- show a count for each review state;
- preserve project and text filters.

## Primary risk

Derived workflow filtering could disagree with the per-note status labels.

## Decision

The same backend review-state derivation drives both filtering and labels.

## Verification

- each filter returns only its requested review state;
- counts match filtered totals;
- awaiting-decision results expose their individual proposals in the detail panel.

## Backend evidence

- `GET /api/notes` accepts `reviewState=queued|checking|awaiting_decision|processed|rejected`;
- `noteStatus` remains supported, while combining two non-empty lifecycle filters returns `400`;
- SQL applies the same terminal-report-first derivation as `NoteWorkflowInfo` to both total and page queries;
- Core tests cover every review state, terminal report overrides, filtered totals, project/search composition, and cursor paging;
- `dotnet test BoyScout.sln --configuration Release`: 59 tests passed;
- `dotnet format BoyScout.sln --verify-no-changes --no-restore`: passed;
- `git diff --check`: passed.

## Not checked in this backend slice

- UI controls and count presentation;
- detail-panel behavior;
- running API or runtime database behavior.
