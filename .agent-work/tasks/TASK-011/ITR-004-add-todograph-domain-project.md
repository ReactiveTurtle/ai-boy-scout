---
id: ITR-004
topic: Add a standalone TodoGraph.Domain project
type: implementation
state: verifying
---

## Expected result

The solution has a dependency-free `TodoGraph.Domain` class library referenced by the blank TodoGraph host, establishing the Domain layer boundary without inventing domain behavior.

## In scope

- Create `src/TodoGraph.Domain/TodoGraph.Domain.csproj` targeting the repository's .NET framework.
- Add a project reference from the TodoGraph host and include the Domain project in the solution.

## Out of scope

- Domain entities, aggregates, value objects, repository ports, or business rules.
- API routes, persistence, or application/infrastructure layers.

## Primary risk

The Domain assembly must remain independent of ASP.NET Core, EF Core, and infrastructure packages.

## Verification

- Inspect the Domain project and host project references.
- Build `BoyScout.sln` to temporary output.

## Verification result

- `TodoGraph.Domain.csproj` targets `net10.0` and has no package or project dependencies.
- `todo-graph/TodoGraph.csproj` references `src/TodoGraph.Domain/TodoGraph.Domain.csproj`.
- `dotnet restore BoyScout.sln --ignore-failed-sources`: passed.
- `dotnet build BoyScout.sln --no-restore -p:OutDir=<temporary path>`: passed with 0 warnings/errors.
