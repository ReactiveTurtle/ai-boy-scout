# ITR-005 - Cut over to the .NET MCP

Status: accepted

## Expected result

Global OpenCode starts the published .NET MCP against a backed-up, version 2 working database with all existing reports preserved.

## In scope

- compatibility for writes from already-running Node MCP processes;
- online SQLite backup;
- Release publication of the .NET MCP;
- global OpenCode MCP command switch;
- working database migration and data verification;
- removal of the superseded Node MCP after successful cutover.

## Out of scope

- project merges;
- API, Angular, categories, themes, and tasks;
- changing existing report content or statuses.

## Primary risk

An old Node MCP process may write during or after migration without populating `project_id`.

## Decision

Install a version 2 compatibility trigger that assigns stable projects to legacy inserts before switching the global command.

## Changed

- added and tested a legacy-insert compatibility trigger;
- added reusable online backup and migration verification scripts;
- published the Release .NET MCP to `artifacts/mcp`;
- switched global OpenCode from Node to the published .NET MCP;
- migrated the working database to schema version 2;
- removed the superseded Node MCP implementation;
- retained the Node MCP client SDK only as a development test dependency.

## Behavioral result

New OpenCode processes run the .NET MCP against the version 2 working database. All pre-migration reports are unchanged, and a report written afterward by an old Node process was linked to a stable project by the compatibility trigger.

## Verification

- online backup created at `%LOCALAPPDATA%\boy-scout-mcp\backups\reports-2026-09-29T09-48-21.537Z.db`;
- backup integrity check passed with 170 reports and sequences 1 through 170;
- the published Release artifact passed the stdio MCP smoke test;
- `opencode mcp list` connects through `dotnet ...\artifacts\mcp\BoyScout.Mcp.dll`;
- the working database is at schema version 2;
- the original 170 report rows match the backup by SHA-256 over all original fields;
- live report `BSR-000171`, written after backup, has a stable project reference;
- 171 live reports, 13 projects, 13 paths, 0 orphan reports, and 0 foreign-key errors;
- full `npm test`, Release .NET tests, formatting, and dependency vulnerability scans passed.

## Not checked

- the already-running current chat using the new MCP process before restart;
- project merge, API, Angular, categories, themes, and tasks.

## Human confirmation

- [x] The working database migration is accepted
- [x] The backup location is understood
- [x] The .NET MCP cutover is accepted
- [x] The API and report-list UI iteration may start

Accepted by the human through the request to build the UI on 2026-09-29.
