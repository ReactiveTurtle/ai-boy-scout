---
id: ITR-008
topic: Create the first TodoGraph Project CQRS/EF/API vertical slice
type: implementation
state: verifying
---

## Expected result

The separate TodoGraph microservice can create and list `Project` aggregates through CQRS handlers, persist them with EF Core SQLite, expose minimal API endpoints, and publish `ProjectCreated` over SignalR.

## Scenarios

1. `POST /api/projects` with a valid name and absolute `FullPath` creates and persists a project with a GUID ID.
2. `GET /api/projects` returns that project after API restart.
3. A connected SignalR client at `/hubs/projects` receives `ProjectCreated` once after successful creation.
4. Invalid name or non-absolute path returns HTTP 400, creates no row, and emits no event.
5. Endpoints are minimal API mappings; no controllers are added.

## Architecture boundary

- `TodoGraph.Domain`: `Project` and domain rules; no ASP.NET, EF, or BoyScout dependency.
- `TodoGraph.Application`: CQRS command/query handlers and repository port. Its `Host/` folder registers Application services and has no HTTP endpoint or SignalR implementation.
- `TodoGraph.Infrastructure`: EF Core SQLite DbContext, migration, and repository adapter.
- `TodoGraph.Api`: separate host, minimal endpoints, SignalR Hub, and composition root; it starts Application and Infrastructure.
- BoyScout and TodoGraph remain separate microservices with no project references between their modules.
- Remove the earlier blank `todo-graph/` host; `npm run start:todo-graph` targets `src/TodoGraph.Api`.

## Data decision

- Use a new database path (`TODO_GRAPH_DB` override; otherwise a `todo-graph-v2` local data directory) so the service does not migrate or modify the old user-local TodoGraph database.
- Do not enforce path uniqueness or check directory/Git existence in this first slice.

## Out of scope

- Other aggregates, project updates/deletion, repository binding, authentication, BoyScout integration, and any UI.

## Primary risks

- Keep Application storage-neutral while hosting DI registration in `Application/Host`.
- Keep SignalR/HTTP concerns in the Api host.
- Ensure the new API uses the new DB path rather than the old TodoGraph database.

## Verification

- Restore and full solution build to temporary output.
- Headless operational API check with an isolated database for POST/GET, invalid input, persistence after restart, and a SignalR `ProjectCreated` event.

## Verification result

- EF Core 10.0.10 `InitialCreate` migration generated for the `projects` table.
- `dotnet build BoyScout.sln --no-restore -m:1 -p:OutDir=<temporary path>` passed with 0 warnings/errors.
- Operational API check on temporary port 5221 and isolated `TODO_GRAPH_DB`: relative `FullPath` returned 400 with no SignalR event; valid creation returned 201 with GUID `Id`; `GET /api/projects` returned the row; a connected SignalR client received the matching `ProjectCreated` payload.
- Restarted the API against the same isolated database; the project remained available. Temporary API processes were stopped.
- The existing user-local TodoGraph database and BoyScout host on port 5187 were untouched.
