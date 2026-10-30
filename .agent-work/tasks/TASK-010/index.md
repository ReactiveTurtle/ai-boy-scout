# TASK-010 — Migrate Todo Graph UI to Angular

## Goal

Move the Todo Graph user interface into the existing Angular `web-ui` application, using standalone components, Angular Router, typed API services, and reusable components.

## Success criteria

- Todo Graph project, task, candidate, graph, and settings screens are Angular-rendered.
- A reusable Angular toolbar is shared across Todo Graph routes.
- Existing Boy Scout routes continue to render their current Angular application.
- Todo Graph project/task context and existing workflow actions remain functional.
- Project identity is handled without selecting or merging distinct projects that share an upstream numeric ID.

## Constraints

- Work only inside the already-open repository; do not browse the rest of the C: drive.
- Preserve all pre-existing working-tree changes and do not commit or push unless asked.
- Todo Graph API currently runs locally on port 5207; the existing Angular app is served by Boy Scout on port 5187 and by the Angular dev server on port 4200.
- Todo Graph backend persistence must use EF Core and follow Domain/Application/Infrastructure/API boundaries.
- BoyScout and TodoGraph remain separate hosts; both use EF Core behind independent Infrastructure adapters and Application-owned persistence ports.
- A `Common` project is limited to shared primitives and versioned module integration contracts; Firebase remains a BoyScout Infrastructure adapter.
- TodoGraph owns an editable DB-backed Process definition with stages/instructions/conditional transitions; one Process aggregate starts at the raw request, links Candidate and Task, and its active stage drives workflow status. No per-run definition snapshots.
- Keep each implementation iteration narrow and verify its stated result.

## Known risks

- `web-ui` currently uses one large Boy Scout standalone component and has no router configured.
- Todo Graph currently serves a separate vanilla-JS UI and API on port 5207.
- Todo Graph data has been observed with two distinct project names sharing source project ID `2`; project identity must be resolved before relying on that ID for task mutations.
- External OpenCode and Git hosting providers are not configured for runtime verification.

## Explicitly excluded

- Unrelated Boy Scout/Firebase changes already present in the working tree.
- Commit, push, or merge operations.
