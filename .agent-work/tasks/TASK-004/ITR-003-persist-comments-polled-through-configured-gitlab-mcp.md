# ITR-003 - Persist comments polled through configured GitLab MCP

**Type:** implementation  
**State:** ready

## Parent task

TASK-004 - Background MR comment monitoring.

## Topic

When a task is in `code_review` with an MR URL, run a background check using a user-configured GitLab MCP in the Todo Graph OpenCode profile and persist newly discovered comments idempotently.

## Expected result

After this iteration, new comments from the configured MCP are durably visible through a Todo Graph API, duplicates are not inserted, and tasks outside `code_review` or without an MR URL are never polled. Confirm with a fake MCP/OpenCode runner and store/API tests.

## Primary risk

MR comment text is untrusted; it must be treated as data in the review prompt. Polling must not reply, resolve discussions, or otherwise mutate the MR.

## Decision

Use OpenCode's existing `opencode run` subprocess as the MCP host. Users configure the GitLab MCP server and credentials in `todo-graph/.opencode/todo-graph.json`; no server package or secret is hardcoded in the application. A dedicated review agent returns a strict normalized JSON result, which the application validates and stores.

## In scope

- Add a normalized MR comment model and durable SQLite storage with duplicate-safe IDs and a per-task poll result.
- Add a background worker that selects only `code_review` tasks with an MR URL and periodically invokes the review agent.
- Add a read-only MCP-enabled OpenCode agent profile and document how users configure a GitLab MCP server under the `gitlab` key.
- Expose a local API for retrieving stored comments.
- Test persistence, deduplication, eligibility, parsing/validation, and polling behavior using a deterministic runner.

## Out of scope

- Comment UI/read marks and user notifications (next iteration).
- Reply drafts or sending replies (separate iteration requiring explicit confirmation).
- Automatically resolving discussions, modifying repositories, or implementing tasks.

## Boundary scenarios

- Eligible task: `code_review` plus valid MR URL -> poll.
- Missing URL or any other task status -> do not poll.
- Repeated provider comment ID -> update/preserve the existing row, never duplicate it.
- Invalid agent JSON/comment identity -> store nothing and record a failed poll.
- Provider outage -> retain existing comments and retry on a later polling cycle.
- Polling agent is explicitly instructed to read only and never write or reply.

## Patch and verification

- Added a background poller (default 60 seconds, configurable from 15 to 3600 seconds) that selects only tasks with `code_review` plus an MR URL.
- Added an OpenCode MR-reader agent that uses a user-configured MCP server named `gitlab`, strict normalized JSON parsing, and idempotent SQLite comment storage.
- Added `GET /api/tasks/{id}/mr-comments` and documented MCP configuration in `README.md`.
- Targeted Todo Graph tests passed: 18/18, including a fake MCP source exercising background eligibility/persistence, comment deduplication, stale-poll rejection, and parser validation.
- Actual GitLab MCP connectivity was not exercised because no `mcp.gitlab` server or credential is configured in this environment.

## Not checked

- User-visible comment refresh/read marks and reply drafting/posting; separate iterations.
- Real provider response shape and actual comment retrieval; depends on the user's MCP server tools and configuration.
