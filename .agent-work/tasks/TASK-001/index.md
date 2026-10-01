# TASK-001 - Separate Boy Scout from Planer and OpenCode configuration

## Goal

Create an independent Boy Scout product under `Desktop\boy-scout` while preserving all existing reports and MCP behavior.

## Success criteria

- Boy Scout source is owned by the standalone project.
- OpenCode connects to the standalone MCP.
- Existing report IDs and database records remain unchanged.
- The MCP CRUD smoke test passes.

## Constraints

- Runtime data remains in `%LOCALAPPDATA%\boy-scout-mcp\reports.db`.
- Do not copy or move a live WAL database.
- Preserve unrelated OpenCode and Playwright configuration.

## Excluded from ITR-001

- .NET and Angular implementation.
- Schema changes.
- Categories, themes, AI processing, and tasks.
