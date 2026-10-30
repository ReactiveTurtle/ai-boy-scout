# ITR-010 - Process new MR comments without duplicating work

**Type:** implementation  
**State:** ready

## Topic

While an MR stays open, treat newly ingested review comments as a new implementation cycle in the existing task worktree. Distinguish the comment-processing phase from MR state and from the local reviewed mark.

## Expected result

New external comments on an open MR queue a review round once. The agent checks whether code changes are needed, asks for clarification when ambiguous, edits and rebuilds if required, then prepares a new local commit and waits for human push. A reply-only comment remains for the human to answer without forcing a commit.

## Scenarios

- An open MR can coexist with `reviewing_comments`, `running`, `committing`, and `awaiting_push` phases.
- Repeated polling of the same comment never duplicates implementation work; new comments arriving mid-cycle wait for the next round.
- Local `reviewed_at` (human read mark) remains independent of `agent_handled_at` (agent's processing mark).
- The agent does not auto-reply or resolve a GitLab discussion; reply drafts require human confirmation as before.
- Worktree, branch and MR URL are reused for subsequent commits; push remains a human action.

## Result and verification

- Open-MR comments are de-duplicated and start a review cycle independently of the execution phase. The developer gets pending comment text in its worktree prompt.
- Comment read marks and agent-handled marks are separate. A `reply_only` cycle returns to Code Review without building or committing; code-changing review rounds build and commit locally, then wait for human push.
- Tests passed for open MR during `in_progress`, open/closed/merged polling eligibility, reply-only completion, code-changing comment review, and non-automatic replies.
- Real GitLab comment rounds remain unverified until the user configures the MCP servers.
