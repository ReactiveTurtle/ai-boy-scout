---
id: ITR-009
topic: Define the built-in task process aggregate and stage transitions
type: decision
state: accepted
---

## Expected result

Choose how the built-in task workflow is represented so EF persistence, agent execution, user prompts, and Angular progress share one domain model.

## User direction

- The domain has process stages and their relationships.
- A process aggregate contains stages.
- Each stage has instructions.
- Stages may use conditional transitions and cycles.
- A stage may ask the user or execute through an agent.
- The task workflow previously discussed becomes a built-in process.

## Proposed model

- TodoGraph owns one `Process` aggregate instance for each task workflow; the aggregate contains the stage instances, instructions, actor (`User`/`Agent`), transition outcomes, current stage, and history.
- The built-in task process is seeded as a database-owned Process definition with editable Stage and transition records/instructions.
- A per-task Process aggregate stores runtime current stage, state, user answers, agent outcomes, and history; it references the Process definition rather than copying its stages/instructions.
- Workflow execution state is derived from the active Process stage; do not keep a second mutable `TaskExecutionStatus` as the source of truth.
- Stage outcomes distinguish automatic continuation, waiting for user/external input, retryable failure, blocked, and terminal completion. Conditional transitions may loop back to earlier stages.
- Editing a definition affects active processes when they next enter a stage; deleting a stage referenced by an active process is rejected.
- Stage transitions are typed domain conditions/outcomes, not executable scripts. Conditional edges may return to earlier stages (for example, agent asks a clarification, user answers, agent resumes).
- User stages wait for an explicit command/input; agent stages are claimed by a worker and receive the stage instruction. Worker outcomes are `Completed`, `NeedsUser`, `RetryableFailure`, or `Blocked`; the aggregate selects the next stage.
- Existing task execution status/progress remains an API projection during migration, derived from the active process stage rather than a second mutable workflow state.
- The initial built-in process covers the established task workflow: clarify/request criteria, propose and approve branch, agent preflight with clarification loop, explicit “К разработке” gate, worktree preparation, implementation with clarification loop, build/rebase, architecture review, commit and human push/MR boundaries.
- The lifecycle starts at a raw request/Candidate and continues through TaskNode creation and implementation. Process has a CandidateId from creation and a nullable TaskId until acceptance criteria are complete; Candidate and Task remain separate entities referenced by Process.

## Human decision

The user clarified that Process and Stage definitions are editable in the database, no per-task definition snapshots are needed while the system is being built, and workflow state must be derived from stages. The agent recorded the active-definition edit rule and protection against deleting a stage referenced by an active run.

## Constraints

- Do not build a generic user-authored workflow designer.
- Do not execute arbitrary transition scripts.
- Preserve the user-approved human gates and existing task/worktree/MR state.
- Keep the process aggregate inside TodoGraph; modules communicate only through integration contracts.
