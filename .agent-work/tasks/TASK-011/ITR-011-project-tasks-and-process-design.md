---
id: ITR-011
topic: Define the desktop ProjectTask and Process workflow
type: decision
state: accepted
---

## Requested desktop scope

- A Project workspace includes a list of its `ProjectTask` items.
- A task can be created from a description alone.
- A `Process` has a name and an ordered list of stages.
- Each stage has instructions/descriptive text for the AI.
- The task is enriched or changed by its Process.

## Designer handoff draft

- Desktop Project list and Project detail/workspace.
- Project detail includes project name/path, task list, empty state, and a create-task form with a description field.
- Process view/editor shows process name, ordered stages, and each stage's AI instructions.
- Task detail shows its description and the Process stage/progress that is currently changing it.
- Include loading, empty, validation, API-error, and task-processing states.

## Decisions

1. Process definitions are a global catalog, not owned by a Project.
2. `ProjectTask` is owned inside its Project aggregate.
3. A task is created from a required Description only. Its Title is empty until AI populates it.
4. A Process is explicitly selected when starting a task; creation does not auto-start a default process.
5. AI may update both Title and Description.
6. A Process is a named, ordered stage list. Each stage has AI instructions and may participate in conditional transitions or wait for user input.
7. Starting a task captures a Process snapshot; later definition changes do not change that task's running workflow.
8. Stage snapshots use SHA-256 over all behavior-affecting content except the stage entity's own Id. Process snapshots store ordered `(StageId, StageHash)` references, so reordering reuses unchanged stage snapshots.

## Topic evolution

- The user reset the implementation after deleting the previous TodoGraph product. Preserve the accepted DDD/condition/waiting principles where they still apply, but the new lifecycle starts with a `ProjectTask` directly rather than a Candidate.
- The new snapshot-at-start choice supersedes the older accepted live-definition/no-snapshot rule.

## Current boundary

Keep `Project` as the aggregate root with GUID identity, Name, and FullPath. Do not add Candidate intake, repository/worktree execution, MR/PR state, or BoyScout integration to this workflow without a separate request.
