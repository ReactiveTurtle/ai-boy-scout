# ITR-005 - Draft and confirm MR replies

**Type:** implementation  
**State:** ready

## Parent task

TASK-004 - Background MR comment monitoring.

## Topic

Let the user prepare a reply to a stored MR discussion comment and explicitly confirm publication through a separately configured GitLab MCP write server.

## Expected result

After this iteration, saving a reply draft has no external effect; only an explicit publish confirmation queues it for the background worker. The user sees sent/failed state, and interruption does not silently retry an uncertain external write. Confirm with store/worker/API tests and a fake reply source.

## Primary risk

An MCP response can be lost after GitLab accepts a reply, so blind retries may create duplicate comments.

## Decision

Use separate MCP server names and permission profiles: `gitlab` for polling with read-only credentials, and `gitlab-write` for reply publication. The reply worker runs only for a confirmed persisted request. Interrupted or failed writes are not automatically retried.

## In scope

- Persist reply drafts and publication state.
- Add an explicit confirmation endpoint and background reply worker.
- Add a dedicated OpenCode reply agent with only `gitlab-write_*` tools available.
- Show draft editing, publication confirmation, and result/error state in the task comment panel.
- Require the reply agent to target the exact stored MR URL and discussion ID and send the exact approved text.

## Out of scope

- Automatic replies without explicit per-comment confirmation.
- Resolving GitLab discussions or changing repository code.
- Automatic retry after an uncertain/failed external write.

## Patch and verification

- Added draft persistence, an explicit confirmation endpoint, and a background publisher queue that uses a separate `gitlab-write` MCP server/agent.
- The queue claims only confirmed drafts on the current MR while the task remains in `code_review`; sending state recovered after restart becomes failed/uncertain and is not retried.
- Added the comment-panel draft editor, publish confirmation prompt, and queued/sending/sent/failed display.
- Full solution tests passed: 314/314. Targeted Todo Graph tests passed: 21/21. OpenCode profile JSON, `node --check todo-graph/wwwroot/graph.js`, and `git diff --check` passed.
- An isolated runtime check verified task status update, comment API response, refusal to confirm an unprepared reply, and availability of the publish-confirmation UI. Fake-publisher tests verified that publication happens only after confirmation.

## Not checked

- Real GitLab MCP read/write operations or browser clicks. This checkout has no configured `gitlab`/`gitlab-write` MCP servers or credentials; provider calls remain unverified until the user configures them.
