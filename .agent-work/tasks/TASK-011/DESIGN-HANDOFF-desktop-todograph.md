# TodoGraph desktop design handoff

## Product boundary

Design the new standalone `TodoGraph.WebApp` for desktop. TodoGraph is a separate microservice from BoyScout. Do not include BoyScout notes, candidates, Firebase settings, or MR/PR review panels.

## Screens to design

1. **Projects list** — project name and local `FullPath`; add-project flow for `Name` + absolute `FullPath`.
2. **Project workspace** — project identity and its `ProjectTask` list, with an empty state and a create-task form containing only a description field.
3. **Process catalog/editor** — global Process definitions. A Process contains one or more ordered ProcessBlocks; every block contains one or more ordered stages. A stage has required AI instructions and may have one backward link to an earlier stage in the same block, with a per-cycle repeat limit.
4. **Start Process flow** — a task does not start automatically. The user explicitly selects one Process; each ProjectTask can have only one ProcessRun total. Starting captures an immutable snapshot of the full block/stage definition.
5. **Task progress/detail** — show the current block and stage, AI-updated task title/description, a user prompt and free-text response when waiting, and completed/blocked states. If a backward link exceeds its repeat limit, show the run as blocked with the error reason.

## Domain behavior to represent

- Projects have an internal GUID identity, a required name, and an absolute local path.
- A `ProjectTask` belongs to a Project and is created from a required description alone. Its title is initially empty until an AI stage supplies one.
- A completed stage must update the task Title or Description; a waiting stage may pause without changing either.
- Process definitions are global. Starting a task captures an immutable definition snapshot; later edits to the catalog do not change the active run.
- Execution is sequential: no branching or parallelism. Blocks and stages advance in order, except for the configured stage backward link.
- A wait shows the stage's prompt; the user submits free text and the same stage resumes. Show the submitted response as provided context if useful, but keep focus on the active prompt and stage.
- Each Process and ProcessBlock must retain at least one child (block/stage respectively); do not present deletion of the final child as an available action.
- A ProcessRun is one-shot per ProjectTask. Do not design a restart/retry action for the same task.
- Stage snapshot hashes are internal. The UI should show only useful active-run status and the configured loop limit, not raw hashes or database identifiers.

## Current API contract

- `GET /api/projects` → project rows `{ id, name, fullPath }`.
- `POST /api/projects` with `{ name, fullPath }`.
- `GET /api/projects/{projectId}/tasks` → task rows `{ id, projectId, title, description }`.
- `POST /api/projects/{projectId}/tasks` with `{ description }`.
- `GET /api/processes` and `GET /api/processes/{processId}` → global Process definitions with ordered Blocks and Stages.
- `POST /api/processes` with `{ name, initialStageInstructions }`; `PUT /api/processes/{processId}` with `{ name }`.
- `POST /api/processes/{processId}/blocks` with `{ initialStageInstructions }`; `PUT /api/processes/{processId}/blocks/order` with `{ blockIds }`; `DELETE /api/processes/{processId}/blocks/{blockId}`.
- `POST /api/processes/{processId}/blocks/{blockId}/stages` with `{ instructions }`; `PUT /api/processes/{processId}/blocks/{blockId}/stages/{stageId}` with `{ instructions }`; `DELETE` the stage route; `PUT .../stages/order` with `{ stageIds }`.
- `PUT /api/processes/{processId}/blocks/{blockId}/stages/{stageId}/backward-link` with `{ targetStageId, maxRepeats }`; `DELETE` that link route.
- Successful catalog mutations return the updated Process DTO; creation returns `201 Created`. ProcessRun start/result/wait-response endpoints are not implemented yet; treat those as intended flows, not current API contract.

## States and desktop layouts

- Projects/workspace: loading, empty, populated, no search results, and API error/retry.
- ProjectTask: title absent before AI enrichment; long descriptions; processing, waiting for user, completed, and blocked with a repeat-limit reason.
- Process editor: empty catalog, one-block/one-stage minimums, multiple blocks, ordered stages, valid/invalid backward links, repeat-limit editing, and validation when attempting to remove the last block/stage.
- ProcessRun: one-shot start, sequential stage/block advancement, waiting prompt → free-text response → same-stage resume, completion, and blocked-at-repeat-limit error. No parallel, branch, restart, or retry UI.
- An active task continues on its captured snapshot while the Process catalog is edited.
- Provide desktop frames for 1440 px and 1280 px widths, plus a clickable prototype for project → task creation → choose Process → task progress.
- Show hover, keyboard focus, selected, disabled, validation, and confirmation states distinctly.

## Deliverables

- Figma link with reusable component styles/tokens and responsive desktop frames.
- Clickable prototype for the principal flows and empty/error states.
- Notes for copy, field labels, and any data/API additions the design requires.
