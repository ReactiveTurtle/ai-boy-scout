# ITR-004 - Display MR comments and mark them reviewed

**Type:** implementation  
**State:** ready

## Parent task

TASK-004 - Background MR comment monitoring.

## Topic

Show polled comments in the Todo Graph task inspector and persist a local reviewed mark per comment.

## Expected result

After this iteration, the user can see comments for the selected task, distinguish unread comments, and mark a comment reviewed without resolving or changing anything in GitLab. Confirm with store tests and an operational UI/API check.

## In scope

- Persist `reviewed_at` for each locally stored MR comment.
- Add an API action to mark a current-MR comment reviewed.
- Render the comment list in the graph inspector and refresh it periodically while open.
- Clearly label the action as local review state, not GitLab thread resolution.

## Out of scope

- Writing or sending replies (next iteration).
- Changing the external GitLab discussion state.

## Patch and verification

- Added persistent per-comment local `reviewed_at`, a mark-reviewed API action, and a comment panel in the task inspector that refreshes every 15 seconds.
- Targeted Todo Graph tests passed: 18/18, including local review-state persistence.
- Operational check on an isolated Todo Graph instance confirmed the current-MR comments API, graph page, and graph script serve the comment panel and local-review control.
- Browser-level button interaction was not exercised.
