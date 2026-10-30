---
id: ITR-006
topic: Define EF Core and Clean Architecture boundaries for Todo Graph
type: decision
state: split
---

## Expected result

Record the backend boundary before replacing the in-progress ADO.NET project identity draft.

## Decision

- **TodoGraph.Domain** owns project identity, candidate/task domain entities, and domain enums/rules; it does not reference EF Core or ASP.NET.
- **TodoGraph.Application** owns project/candidate/task use cases and repository ports; it references Domain only.
- **TodoGraph.Infrastructure** owns EF Core SQLite `DbContext`, entity configurations, migrations, repository implementations, and persistence adapters; it implements Application ports.
- The existing **TodoGraph API host** owns Minimal API routes, workers, configuration, and dependency-injection composition; it depends on Application and Infrastructure, and application/workers depend on ports rather than the concrete EF context.
- Preserve the existing SQLite database and workflow state through an explicit EF migration; do not replace or reset the user's database.
- Boy Scout publication remains an external integration boundary and supplies the source database namespace plus source project ID.

## Human direction

The user requested EF and clean architecture on the backend, then confirmed continuing with internal project IDs and a safe migration.

## Verification

The initial EF boundaries for Todo Graph were recorded, but the user then requested domain design for both BoyScout and TodoGraph and proposed a modular monolith. No EF packages or schemas were changed in this decision iteration.

## Topic evolution

- Initial scope: EF/clean architecture inside the separate Todo Graph host.
- New direction: model the domains and module boundaries for both BoyScout and TodoGraph, potentially under one host.
- Action: split the host/module decision from implementation. Confirm whether to consolidate the hosts and whether EF applies to both data modules before changing deployment or storage ownership.
