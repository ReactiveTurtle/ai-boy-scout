# TASK-004 - Background MR comment monitoring

## Goal

When a Todo Graph task is in `Code Review`, poll its linked MR in the background for new review comments, surface them to the user with durable local review marks, and support human-approved replies.

## Success criteria

- A Todo Graph task can carry an MR URL and enter `Code Review`.
- Only tasks in `Code Review` are polled by the Todo Graph background worker.
- Newly observed comments are persisted and shown to the reviewer.
- Review state is durable and distinguishable from resolving a discussion in the code host.
- Replies are sent to the correct MR discussion with an explicit, agreed authorization policy.
- Polling and comment/reply behavior are verified against the applicable provider or a deterministic fake.

## Constraints

- The current repository has no MR-provider integration; `todo-graph` currently states its agent does not interact with MRs.
- The user clarified that the background behavior is polling for new comments, not running code changes.
- The user selected MCP for code-host interaction, an MR URL on each task, local seen/reviewed marks, and human-approved replies.
- The user specified that MR polling starts when task status is `Code Review`.
- The specific provider MCP server/tools and how a persistent background worker can invoke them are not yet specified.
- Do not publish over or terminate active MCP processes as part of this work.

## Known risks

- Polling multiple projects or MRs requires durable cursor/state and duplicate-safe ingestion.
- Posting replies or resolving code-host discussions has external side effects and needs an explicit human approval policy.
- A stdio MCP server attached to an interactive OpenCode session is not automatically callable by the Boy Scout API background worker.

## Explicitly excluded until separately requested

- Automatically implementing tasks or pushing commits (now requested as a separate task; see TASK-005).
- Automatically resolving MR discussions without an agreed policy.

## Split decision

The user separately requested a UI action to queue tasks for implementation and visible execution/build states. That is independent from MR comment polling and introduces repository access, execution permissions, commit behavior, and project-build orchestration. Track it as TASK-005; do not expand this MR review task implicitly.

## Current implementation result

- Task status/MR URL controls, configurable OpenCode MCP-based polling, local comment review marks, and human-confirmed reply drafts/publication are implemented in the working tree.
- Deterministic tests and local API/runtime checks pass.
- Real GitLab MCP reads/writes remain unverified until the user adds enabled `gitlab` and `gitlab-write` servers and credentials to `todo-graph/.opencode/todo-graph.json` (credentials should be supplied via environment variables).
- The already-running Todo Graph process has the previous assembly loaded; it must be restarted to run this working-tree version.
