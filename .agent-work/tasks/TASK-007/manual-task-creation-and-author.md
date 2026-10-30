# TASK-007 - Create tasks manually in Todo Graph

## Goal

Allow the user to create a task directly in Todo Graph from the project UI, without a Boy Scout candidate or imported task.

## Requirements

- Create from a selected existing Todo Graph project.
- The selected project retains its configured GitLab/GitHub provider and repository mapping.
- Require a title and description; support acceptance criteria and consequences.
- Record the configured Todo Graph display name as the manual task's creator and show it in the UI.
- New tasks start idle/not queued with no MR and no build state.
- Backward-compatible additive SQLite migration; imported tasks keep their current source IDs and fields.
- The new task can then use the same project repository mapping, queue, clarification and execution workflow.

## Current state

Manual task creation and creator display are the current remaining implementation item after the MCP settings and graph board changes.
