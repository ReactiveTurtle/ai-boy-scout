# ITR-001 - Discover task execution prerequisites

**Type:** discovery  
**State:** exploring

## Topic

Identify which task lifecycle data and project context are available to implement a UI-driven execution queue safely.

## Expected result

Establish whether Todo Graph can locate a repository and which task execution operations remain undefined, confirmed by source inspection.

## Verified facts

- `todo-graph/CandidateAgentWorker.cs` only processes candidate analysis jobs; it does not execute task code.
- `todo-graph/TodoGraphAgent.cs` uses a triage OpenCode agent and its project profile denies all tools.
- `todo-graph/Models.cs` currently has only task status text; there is no execution-job or build-result model.
- `src/BoyScout.Core/Tasks/TodoGraphPublisher.cs` sends `report.Project` as `ImportedTask.Project`; it does not send a repository directory.
- The graph inspector has a status selector, but `in_progress` only updates the stored task state and does not enqueue any worker.

## Topic evolution

The first MR-monitoring task was deliberately limited to comments. The user has now expanded the request to UI-driven full-cycle task execution in a Git worktree, through an MR, with visible MR-comment/commit phases and a separate build indicator. The task may also be rejected into `closed`. Before development, the agent must explain the problem, risks, and corner cases in plain language (an HTML page is acceptable), then ask only unresolved requirements questions. Do not ask the user to prove they understood. Keep this work in TASK-005.

## Unknowns

- Whether a task must have an existing MR before it can be queued.
- Whether the worker modifies a local checkout, creates a branch/commit, pushes, or opens an MR.
- Whether those external repository effects need step-by-step human approval.
- How the user configures a project checkout path in Todo Graph.
- Whether build/test failure blocks pushing or MR creation.

## User decisions

- Queueing from the UI starts the full implementation cycle through an MR.
- Code runs in an isolated Git worktree.
- Rejected tasks become `closed`.
- Before development, the agent explains the problem, risks, and corner cases in plain language; HTML is acceptable. It asks only genuinely unresolved requirements questions and does not quiz the user about understanding.
- After requirements clarification, the agent may propose decomposition, but child tasks require user approval.
- Changes should remain backward compatible unless a breaking exception is explained and approved.
- Build failures trigger root-cause investigation, repair, and another build; MR creation waits for a successful build.
- The user chose to continue repair/build cycles without a fixed retry limit; stop only when an external/blocking condition prevents progress.
- During implementation cycles, diagnose errors before edits and ask the user when the cause or safe fix is ambiguous; the preferred outcome is to resolve uncertainty before the first code change.
