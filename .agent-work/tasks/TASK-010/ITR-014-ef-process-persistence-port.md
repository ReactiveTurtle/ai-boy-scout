---
id: ITR-014
topic: Persist editable Process definitions and runtime state through EF Core
type: implementation
state: verifying
---

## Expected result

TodoGraph.Application owns a process persistence port; TodoGraph.Infrastructure implements it with EF Core SQLite and can round-trip editable Process definitions and ProcessRun state/history without storing a per-run definition copy.

## In scope

- Create TodoGraph.Application and TodoGraph.Infrastructure projects with dependency direction Domain -> none, Application -> Domain/Common, Infrastructure -> Application/Domain/EF.
- Add EF mappings/migration for ProcessDefinition, nested Stage/Transition definitions, ProcessRun, and runtime events.
- Add a process repository port/implementation and a SQLite integration test for definition edits, stage transitions, and runtime state persistence.
- Keep the current two hosts and existing SQLite data file; no `EnsureCreated` on the existing live database.

## Out of scope

- Converting all CandidateStore/TaskNode queries in this iteration.
- BoyScout EF persistence conversion.
- Angular process editor or process UI.
- Copying process definitions/instructions into each ProcessRun.

## Primary risk

EF mappings for private aggregate collections and the existing SQLite database schema can diverge; use isolated SQLite tests and explicit EF migrations.

## Verification

Run the new Infrastructure integration tests, the Domain tests, and a TodoGraph API build to temporary output while port 5207 remains active.

## Verification result

- `TodoGraph.Infrastructure.Tests.ProcessRepositoryTests`: 2/2 passed with EF SQLite migrations, editable process instructions, ProcessRun stage/history round trip, idempotent built-in definition seeding, and preservation of a pre-existing `candidates` table.
- `TodoGraph.Domain.Tests.ProcessAggregateTests`: 4/4 passed.
- `dotnet build todo-graph/TodoGraph.csproj --no-restore -p:OutDir=<approved-temp-dir>`: passed with 0 warnings/errors.
- The live TodoGraph process was not stopped; all backend build/test outputs avoided its locked normal assembly.
