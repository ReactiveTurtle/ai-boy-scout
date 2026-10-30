# ITR-009 - Create an MR after verified human push

**Type:** implementation  
**State:** ready

## Topic

Once the human has pushed and the remote branch tip equals the saved SHA, create or find the task MR through the user-configured GitLab MCP.

## Expected result

An existing open MR is reused. For a new task, the agent first looks for an MR by source branch to avoid duplication, then creates one and saves its URL in separate open-MR state. No MR is created if the push is missing, changed or the MCP is not configured.

## Primary risk

The MR can be created remotely while the local process loses its response; interrupted creation must be marked for reconciliation, not blindly retried.

## Boundary

- Never push. Never auto-merge.
- The GitLab MCP server/credentials are user-configured; real provider operations cannot be verified until available.

## Result and verification

- A worker rechecks that the exact commit is still on the remote before invoking the user-configured GitLab MCP; the MR agent first searches by source branch and only creates when absent.
- Existing open MR URLs are reused. Interrupted or uncertain creation enters `mr_retry_required` for human reconciliation; no blind duplicate request is made.
- Targeted tests use a fake MR creator plus real local Git ref verification and validate an MR URL stored independently of task phase. Actual GitLab MCP calls remain unverified until configured.
