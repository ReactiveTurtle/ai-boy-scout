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
6. A Process is a named sequence of ProcessBlocks. Each block contains ordered stages; execution has no branches or parallelism. A bounded backward link may connect stages.
7. Starting a task captures a Process snapshot; later definition changes do not change that task's running workflow.
8. Stage snapshots use SHA-256 over all behavior-affecting content except the stage entity's own Id. ProcessBlock snapshots store ordered `(StageId, StageHash)` references, and the Process snapshot stores ordered blocks.

## Topic evolution

- The user reset the implementation after deleting the previous TodoGraph product. Preserve the accepted DDD/condition/waiting principles where they still apply, but the new lifecycle starts with a `ProjectTask` directly rather than a Candidate.
- The new snapshot-at-start choice supersedes the older accepted live-definition/no-snapshot rule.

## Follow-up decisions and open rules — 2026-10-30

- A Process must contain at least one ProcessBlock, and each ProcessBlock must contain at least one Stage.
- The user first selected predicate-based transitions, then clarified the current workflow should have no branching or parallelism; predicate execution is deferred and ITR-017 is superseded.
- A waiting stage presents a prompt, accepts free-text user input, and resumes that same stage.
- A ProjectTask starting a Process captures its full block graph at task start.
- Backward links connect a stage to an earlier stage in the same ProcessBlock. Each link has a per-cycle repeat limit; exceeding it blocks the run with an error.
- The latest user clarification is to implement the simple sequential workflow without parallel execution or branching.
- The user clarified that blocks connect within a Process; this supersedes the earlier interpretation that one Process connects to another.

## Current boundary

Keep `Project` as the aggregate root with GUID identity, Name, and FullPath. Do not add Candidate intake, repository/worktree execution, MR/PR state, or BoyScout integration to this workflow without a separate request.
