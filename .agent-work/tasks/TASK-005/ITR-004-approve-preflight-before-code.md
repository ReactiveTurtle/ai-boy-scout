# ITR-004 - Require approval of clarification before code changes

**Type:** implementation  
**State:** ready

## Topic

After reading the task explanation and answering open requirements questions, explicitly approve a task for implementation.

## Expected result

Tasks with a saved preflight remain paused until the user presses Start development; unanswered questions block approval. A rejected task cannot be approved. Approval records a durable `ready_for_development` status; it must never directly edit code.

## Scope

- API route for explicit preflight approval with validation of required answers.
- UI approval button and concise explanation of the next worktree/build steps.
- Targeted tests for no-questions, unanswered, answered and rejected paths.

## Boundary

- Creating worktrees, modifying code, building, committing, pushing and opening an MR belong to the next implementation iteration.

## Result and verification

- Added explicit approval route and inspector button; unanswered questions and closed/non-awaiting tasks cannot be approved.
- Approval advances a task only to durable `ready_for_development`; it does not claim that implementation started.
- Targeted Todo Graph tests passed: 27/27. Full solution: 320/320; JS/config syntax and `git diff --check` passed.
- An isolated runtime check verified the queue worker failure route when OpenCode is unavailable. Successful approval was verified by the store tests with and without requirements questions, but not by a live browser click.
