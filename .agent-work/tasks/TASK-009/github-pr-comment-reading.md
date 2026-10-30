# TASK-009 - Read GitHub PR comments in the shared queue workflow

## Goal

Let a project select GitHub, use a user-configured read-only GitHub MCP to read PR state/source branch/comments, and feed those comments into the same task review workflow.

## User decisions

- Select the code host per project in Todo Graph settings.
- GitHub MCP only reads; the user pushes and creates the GitHub PR manually.
- GitLab keeps its separate read-only and write server settings.

## Scope

- Add an encrypted, local stdio GitHub read MCP configuration on the settings page with exact read-tool allowlist.
- Persist project code host and route polling to `gitlab` or `github` MCP based on that selection.
- Normalize GitHub PR state, source branch and comments to the existing MR-review model.
- For GitHub, disable MCP reply posting and MR/PR creation; show manual instructions for linking a user-created PR.
- Preserve GitLab behavior and old settings through additive migrations.

## Boundary cases

- Existing projects default to GitLab for compatibility.
- GitHub PR with missing source branch cannot start a worktree until read MCP resolves it.
- Closed/merged PR stops polling and blocks worktree execution.
- No GitHub write MCP or push tool is exposed to an agent.
