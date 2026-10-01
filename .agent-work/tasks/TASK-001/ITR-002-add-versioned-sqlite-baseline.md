# ITR-002 - Add versioned SQLite baseline

Status: accepted

## Expected result

The standalone project has a .NET Core library that creates or adopts the legacy Boy Scout SQLite schema as version 1 without changing existing reports, confirmed by tests against new and populated legacy databases.

## In scope

- .NET solution and Core library;
- SQLite connection setup;
- version 1 migration and schema validation;
- tests for new, populated legacy, and incompatible databases.

## Out of scope

- running migration against the working AppData database;
- normalized projects;
- categories, themes, tasks, API, Angular, and .NET MCP;
- changes to the Node MCP.

## Primary risk

Marking an incompatible legacy schema as migrated could hide data loss or break a later host.

## Decision

Validate all legacy report columns before recording migration version 1, and fail without modifying an incompatible database.

## Changed

- added `BoyScout.sln` and the `BoyScout.Core` library;
- added SQLite connection setup with foreign keys, busy timeout, and WAL after validation;
- added version 1 migration history through `PRAGMA user_version` and `schema_migrations`;
- added tests for a new database, populated legacy database, and incompatible schema;
- pinned a non-vulnerable SQLite native bundle version.

## Behavioral result

A new database can be initialized as version 1, and an existing compatible report database can be adopted without changing report rows or the AUTOINCREMENT sequence. An incompatible table is rejected before migration metadata is written.

## Verification

- `dotnet test BoyScout.sln --configuration Release`: 3 tests passed;
- populated legacy test preserved sequence 168 and assigned 169 to the next report;
- `dotnet list BoyScout.sln package --vulnerable --include-transitive`: no vulnerable packages;
- the Node MCP smoke test still passes;
- the working AppData database remains at `user_version = 0` and was not migrated;
- the working database retained all columns and advanced independently from report 168 to 169 during the iteration.

## Not checked

- migration of the working AppData database;
- concurrent migration attempts from multiple processes;
- normalized project, category, theme, task, API, UI, or .NET MCP behavior.

## Human confirmation

- [x] The version 1 baseline is understandable
- [x] Existing report preservation is sufficiently verified
- [x] Failure behavior for incompatible schemas is accepted
- [x] The normalized-project migration iteration may start

Accepted by the human through the instruction to continue on 2026-09-29.
