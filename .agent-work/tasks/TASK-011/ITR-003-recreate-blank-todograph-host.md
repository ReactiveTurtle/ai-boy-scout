---
id: ITR-003
topic: Replace TodoGraph with a blank ASP.NET Core host
type: implementation
state: verifying
---

## Expected result

All TodoGraph application/module/UI/test source is removed from the repository, and `todo-graph/` contains only a blank ASP.NET Core web project registered in the solution.

## In scope

- Remove the old `todo-graph/` host and vanilla UI.
- Remove `src/TodoGraph.*`, `tests/TodoGraph.*`, and Angular TodoGraph components/routes.
- Keep `BoyScout.Common.Persistence.IUnitOfWork` and historical `.agent-work` records.
- Add a blank web template project at `todo-graph/TodoGraph.csproj` and keep its npm start script.

## Out of scope

- Deleting the user-local TodoGraph SQLite database outside the repository.
- Stopping/restarting the separate BoyScout host on port 5187.

## Verification result

- `dotnet restore BoyScout.sln --ignore-failed-sources`: passed.
- `dotnet test BoyScout.sln --no-restore -p:OutDir=<temporary path>`: passed 296/296.
- `dotnet build BoyScout.sln --no-restore -p:OutDir=<temporary path>`: passed with 0 warnings/errors, including the blank TodoGraph host.
- Operational check of the blank host on temporary port 5220: `GET /` returned HTTP 200 and `Hello World!`; the temporary process was stopped.
- Port 5207 is free after the user-authorized stop of PID 33620. Port 5187 remains owned by PID 77696 and was not stopped.
