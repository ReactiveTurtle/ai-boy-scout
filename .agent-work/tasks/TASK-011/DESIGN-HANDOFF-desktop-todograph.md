# TodoGraph desktop design handoff

## Product boundary

Design the new standalone `TodoGraph.WebApp` for desktop. TodoGraph is a separate microservice from BoyScout. Do not include BoyScout notes, candidates, Firebase settings, or MR/PR review panels.

## Screens to design

1. **Projects list** — project name and local `FullPath`; add-project flow for `Name` + absolute `FullPath`.
2. **Project workspace** — project identity and its `ProjectTask` list, with an empty state and a create-task form containing only a description field.
3. **Process catalog/editor** — global process definitions, process name, and an ordered list of stages. Each stage has an AI instruction/description; transitions may branch or wait for user input.
4. **Start Process flow** — a task does not start automatically. The user explicitly selects a Process when starting it.
5. **Task progress/detail** — show current stage/progress, AI-enriched task title/description, waiting-for-user prompts, and terminal/retryable states.

## Domain behavior to represent

- Projects have an internal GUID identity, a required name, and an absolute local path.
- A `ProjectTask` belongs to a Project and is created from a required description alone. Its title is initially empty until an AI stage supplies one.
- AI stages may update both task title and description.
- Process definitions are global. Starting a task captures a definition snapshot; later edits to the catalog do not change an already-started task.
- Stage snapshots are content-addressed. Reordering a process should reuse unchanged stage snapshots; a behavior change creates a different snapshot. This is primarily an implementation detail; expose version/snapshot information only if it helps the user understand an active task.

## Current API contract

- `GET /api/projects` → project rows `{ id, name, fullPath }`.
- `POST /api/projects` with `{ name, fullPath }`.
- `GET /api/projects/{projectId}/tasks` → task rows `{ id, projectId, title, description }`.
- `POST /api/projects/{projectId}/tasks` with `{ description }`.
- Process catalog/start endpoints are not implemented yet; show the intended interaction in the prototype without assuming endpoint details.

## States and desktop layouts

- Projects/workspace: loading, empty, populated, no search results, and API error/retry.
- ProjectTask: title absent before AI enrichment; long descriptions; processing, waiting for user, completed, retryable failure, and blocked presentation.
- Process editor: one-stage and multi-stage definitions; reordered stages; branching/awaiting-user transitions; an active task stays on its starting snapshot while the catalog is edited.
- Provide desktop frames for 1440 px and 1280 px widths, plus a clickable prototype for project → task creation → choose Process → task progress.
- Show hover, keyboard focus, selected, disabled, validation, and confirmation states distinctly.

## Deliverables

- Figma link with reusable component styles/tokens and responsive desktop frames.
- Clickable prototype for the principal flows and empty/error states.
- Notes for copy, field labels, and any data/API additions the design requires.
