# TASK-011 — Rebuild TodoGraph as an isolated microservice

## Goal

Retire the old TodoGraph product and BoyScout integration, then rebuild TodoGraph as a separate microservice with DDD, CQRS, EF Core, minimal API endpoints, and SignalR.

## Success criteria

- The legacy TodoGraph host, UI, tests, and BoyScout integration are removed.
- BoyScout no longer publishes to TodoGraph or exposes TodoGraph-specific report statuses/APIs.
- TodoGraph has independent Domain, Application, Infrastructure, Api, and WebApp projects; it does not reference BoyScout modules.
- `TodoGraph.Api` is a separate minimal-API/SignalR host that starts Application and Infrastructure.
- `TodoGraph.WebApp` is a separate standalone Angular application alongside the API.
- Application owns CQRS commands/queries and ports, with service registration under its `Host` folder.
- `Project` is an aggregate root with a generated GUID identity, required trimmed `Name`, and required absolute `FullPath`.
- Existing BoyScout databases upgrade safely; migration history remains valid and obsolete TodoGraph delivery records are removed by a new migration.
- The existing TodoGraph database outside this repository is untouched.

## Constraints

- Preserve unrelated working-tree changes; no reset, commit, or push.
- Do not terminate processes without user approval. The user explicitly authorized stopping the old process on port 5207 (PID 33620); it was stopped before replacing the folder.
- Do not inspect or delete the user-local TodoGraph database outside this repository.
- Work within the repository and approved temporary-output directory only.

## Primary risks

- The old host initially ran from the folder scheduled for replacement; this file-lock risk was cleared after the user authorized stopping PID 33620.
- BoyScout reports, MCP status updates, UI filters, smoke tests, and database migrations reference TodoGraph.

## Explicit scope

- Remove the old `todo-graph/`, `src/TodoGraph.*`, `tests/TodoGraph.*`, and `web-ui` TodoGraph screens/toolbars.
- Rebuild under `src/TodoGraph.Domain`, `src/TodoGraph.Application`, `src/TodoGraph.Infrastructure`, and `src/TodoGraph.Api`.
- Remove BoyScout publication contracts, publisher, MCP/API status behavior, and related UI/tests.
- Keep Common persistence contracts used by BoyScout and preserve historical migration records for database compatibility.
- Keep `.agent-work` records as historical task notes.
