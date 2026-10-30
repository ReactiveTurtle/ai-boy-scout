# ITR-006 - Separate MR state from execution phase

**Type:** implementation  
**State:** ready

## Topic

Represent whether a task's MR is open independently of task status and execution phase.

## Expected result

An open MR continues to be polled for comments while the task is developing/committing; closing/merging the MR stops polling. Existing `code_review` tasks with URLs migrate to open MR state, preserving the original task status.

## Scenarios

- Legacy task: status `code_review` and MR URL -> MR state `open`; continue polling.
- New task with MR URL + phase `committing` -> MR state `open`; continue polling.
- Task with URL but MR state `closed` or `merged` -> no polling/reply publication.
- Task without URL -> MR state `none`, no polling.
- Rejecting the task closes task execution while leaving the MR history visible; no new replies or comment polling for a rejected task.

## Boundary

- Do not alter existing task status values or delete review history.
- MR state changes do not trigger commits, pushes or MR operations.

## Result and verification

- Added persisted MR state, separate UI/API control, and legacy migration. Polling and reply eligibility now use an open MR and URL, not the task's `code_review` status.
- Targeted tests cover open MR with task `in_progress`, closed/merged poll suppression, reply gating and migration.
- An isolated HTTP check changed an `in_progress` task's MR from `open` to `closed` and confirmed `/api/graph` retained the task phase independently.
