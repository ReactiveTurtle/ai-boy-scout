# ITR-013 - Require agent-confirmed rejection

Status: verifying

## Expected result

A user can propose a rejection reason, but a note is rejected only when OpenCode confirms that reason against the permitted project.

## In scope

- persist the user's proposed rejection reason and review intent;
- queue the note for an OpenCode rejection review;
- reject only on an `obsolete` verdict;
- keep the note active and show the agent's argument on `current` or `unverified`;
- allow the user to revise and resubmit the reason;
- show the submitted reason and agent confirmation in note details.

## Out of scope

- rejecting without project consent;
- overriding an agent disagreement without another review;
- free-form chat with the agent;
- changing task confirmation behavior.

## Primary risk

A generic relevance verdict could accidentally reject a note without validating the user's exact reason.

## Decision

Rejection is a persisted review intent. The prompt explicitly asks OpenCode to validate the submitted reason; only `obsolete` performs the terminal rejection. Other verdicts return the note to review with the agent's own argument.

## Verification

- migration preserves existing proposals and reports;
- rejection review requires a non-empty reason and project consent;
- `obsolete` dismisses the report and stores both user and agent reasons;
- `current` and `unverified` keep the report open and expose the agent argument;
- a revised reason can be submitted again.

## Evidence

- `dotnet test BoyScout.sln --configuration Release`: 37 tests passed;
- `npm test`: 37 tests passed, solution build completed with 0 warnings and 0 errors, MCP smoke passed against schema v6;
- `dotnet format BoyScout.sln` completed and `git diff --check` reported no whitespace errors;
- migration coverage verifies existing v5 proposals become `proposal` intent with a null rejection reason;
- store and service coverage verifies consent, validation, resubmission, exact reason retention, and all three relevance branches.
- Angular note details now submit a concrete rejection reason only after checking project consent, explain that rejection requires OpenCode confirmation, and keep the submitted reason separate from agent verification;
- unconfirmed rejection reviews can be edited and resubmitted, while rejected notes expose the submitted reason, verification, and restore action;
- `npm run build` completed successfully for `web-ui` (with the existing component-style budget warning: 19.98 kB against 18.00 kB).

## Intentionally unchanged

- runtime database under `%LOCALAPPDATA%`;
- projects outside the Boy Scout repository.
