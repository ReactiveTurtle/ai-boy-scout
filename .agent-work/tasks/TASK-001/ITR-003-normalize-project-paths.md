# ITR-003 - Normalize project paths

Status: accepted

## Expected result

Schema version 2 gives every legacy report a stable project identity and stores filesystem paths separately, confirmed by migration and idempotency tests.

## In scope

- `projects` and `project_paths` tables;
- nullable `reports.project_id` compatibility column;
- deterministic backfill from legacy project paths;
- support for several paths per project at the schema level;
- version 2 validation and tests.

## Out of scope

- automatically deciding that different worktrees belong to one repository;
- project merge commands and UI;
- migration of the working AppData database;
- categories, themes, tasks, API, Angular, and .NET MCP.

## Primary risk

Path normalization could merge distinct legacy values or leave reports without a stable project reference.

## Decision

Normalize equivalent spellings of the same absolute path case-insensitively, but keep genuinely different paths as separate projects until a human merges them.

## Changed

- added schema version 2 with `projects` and `project_paths`;
- added the compatibility `reports.project_id` reference and project/status index;
- backfilled projects deterministically by first report sequence;
- collapsed case and trailing-separator variants of the same path;
- retained distinct worktree paths as independent projects;
- retained the original `reports.project` text unchanged.

## Behavioral result

Every report in a migrated database has a stable project reference. A project can own several paths, while ambiguous worktrees remain separate until an explicit future merge.

## Verification

- `dotnet test BoyScout.sln --configuration Release`: 4 tests passed;
- repeated migration is idempotent;
- equivalent path variants resolve to one project;
- a distinct worktree resolves to another project;
- an additional non-primary path can be attached to a project;
- `dotnet format BoyScout.sln --verify-no-changes --no-restore` passed;
- NuGet vulnerability scan found no vulnerable packages;
- the Node MCP smoke test still passes;
- the working AppData database remains unchanged at `user_version = 0` with 169 reports.

## Not checked

- automatic Git remote/worktree detection;
- project merge operations;
- migration of the working AppData database;
- API, UI, categories, themes, tasks, and .NET MCP.

## Human confirmation

- [x] The project/path distinction is understandable
- [x] Conservative worktree handling is accepted
- [x] Legacy project text preservation is accepted
- [x] The .NET MCP iteration may start

Accepted by the human through the instruction to continue on 2026-09-29.
