# ITR-004 - Add .NET MCP parity

Status: accepted

## Expected result

A .NET stdio MCP exposes the same four report tools and behavior as the Node server, confirmed by a real MCP client against an isolated legacy database.

## In scope

- typed report store in `BoyScout.Core`;
- .NET stdio MCP host;
- `boy_scout_add`, `boy_scout_list`, `boy_scout_get`, and `boy_scout_update_status`;
- migration of temporary legacy databases to version 2;
- protocol-level parity test.

## Out of scope

- switching global OpenCode to the .NET MCP;
- migrating the working AppData database;
- removing the Node MCP;
- project merge, categories, themes, tasks, API, and Angular.

## Primary risk

Protocol or result-shape differences could make the replacement incompatible with existing agents.

## Decision

Use the official C# MCP SDK and retain the existing tool names, parameter names, JSON text shape, statuses, and default limits.

## Changed

- added a typed `ReportStore` to `BoyScout.Core`;
- added the `BoyScout.Mcp` .NET stdio host using the official MCP SDK;
- implemented the four existing report tools;
- preserved snake_case tool parameters and report JSON;
- exposed the five statuses as an MCP JSON Schema enum;
- serialized concurrent project creation with an immediate SQLite transaction;
- added a protocol-level smoke test driven by the Node MCP client SDK.

## Behavioral result

The .NET server migrates an isolated legacy database to version 2 and supports add, list, get, status update, stable IDs, project filtering, and explicit not-found tool errors with the existing external contract.

## Verification

- `npm run test:mcp:dotnet` passed through a real stdio MCP client;
- the test discovered exactly the four expected tools;
- snake_case parameter names and status enum were verified;
- legacy `BSR-000001` survived migration and the next report received `BSR-000002`;
- list, get, update, and missing-report behavior passed;
- all migrated reports had stable project references;
- `dotnet test BoyScout.sln --configuration Release`: 4 tests passed;
- `dotnet format BoyScout.sln --verify-no-changes --no-restore` passed;
- NuGet and npm vulnerability scans found no vulnerable packages;
- the existing Node MCP smoke test passed;
- the working AppData database was not migrated and independently advanced to 170 reports.

## Not checked

- global OpenCode running the .NET MCP;
- migration and backup of the working AppData database;
- coexistence with an already-running old Node MCP process after cutover;
- project merge, API, UI, categories, themes, and tasks.

## Human confirmation

- [x] The .NET MCP contract is accepted
- [x] Protocol-level verification is sufficient
- [x] The working database may be backed up and migrated
- [x] Global OpenCode may switch from Node to .NET MCP

Accepted by the human through the instruction to proceed on 2026-09-29.
