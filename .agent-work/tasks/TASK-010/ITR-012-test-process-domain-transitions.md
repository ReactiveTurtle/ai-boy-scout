---
id: ITR-012
topic: Pin Process domain outcomes before implementation
type: test
state: accepted
---

## Expected result

Domain tests describe a Process definition containing stages/instructions/transitions and a per-request Process aggregate that begins on a raw Candidate, pauses for a user, loops back to an agent, and links a Task only after criteria are established.

## In scope

- Create the TodoGraph.Domain project/test boundary.
- Add tests for actor enforcement, conditional stage transitions/cycles, user wait/resume, TaskId-null-before-criteria, and linking Task after the criteria stage.
- Keep the Domain independent of EF, ASP.NET, Firebase, and SQLite.

## Out of scope

- EF mappings, database migrations, API routes, workers, or Angular process UI.
- A generic scripting engine or user-authored process designer.
- BoyScout process definitions in this iteration.

## Primary risk

Duplicating workflow state in TaskExecutionStatus and Process state. Tests must assert Process stage/state as the source of truth; legacy statuses will be compatibility projections.

## Verification

- Red: the new test project initially failed because the `TodoGraph.Domain.Processes` types did not exist.
- Green: `dotnet test tests/TodoGraph.Domain.Tests/TodoGraph.Domain.Tests.csproj --filter FullyQualifiedName~ProcessAggregateTests`: 4/4 passed.
- `dotnet build todo-graph/TodoGraph.csproj --no-restore -p:OutDir=<approved-temp-dir>`: passed with 0 warnings/errors while the normal API output is locked by the live process.
- Process domain has no EF or ASP.NET dependency; the TodoGraph API host now references `TodoGraph.Domain`.
