# ITR-001 - Relocate the existing MCP

Status: accepted

## Expected result

The existing four MCP tools run from `Desktop\boy-scout` against the unchanged AppData database, confirmed by an isolated CRUD smoke test and OpenCode MCP connection check.

## In scope

- standalone Node package;
- current MCP server and smoke test;
- Boy Scout skill source;
- global OpenCode path updates;
- removal of the superseded source copy after verification.

## Out of scope

- behavior changes;
- database schema migration;
- report processing and UI;
- .NET rewrite.

## Primary risk

Switching paths while another OpenCode process uses the old server could leave two implementations active temporarily.

## Decision

Keep the database in AppData and verify the new server before removing the old source copy.

## Changed

- created the standalone `Desktop\boy-scout` package;
- moved MCP and skill ownership into the standalone project;
- updated global OpenCode MCP and skill paths;
- removed the superseded global source copy.

## Intentionally unchanged

- SQLite schema and location;
- report IDs and content;
- MCP tool contracts;
- report processing behavior.

## Verification

- `npm test` passed against an isolated temporary database;
- `node --check` passed for the server and smoke test;
- `opencode mcp list` connected to `Desktop\boy-scout\src\mcp\server.mjs`;
- `opencode debug skill` resolved the skill from the standalone project;
- the AppData database still contains 168 reports with sequences 1 through 168;
- `npm audit --omit=dev` reported no vulnerabilities in the standalone package.

## Not checked

- long-running concurrent writes from several OpenCode sessions;
- schema evolution, categories, themes, tasks, or UI;
- replacement of the Node MCP with .NET.

## Human confirmation

- [x] The standalone project boundary is accepted
- [x] Existing MCP behavior is preserved
- [x] Verification matches the relocation result
- [x] The next schema and .NET iteration may start

Accepted by the human through the instruction to continue on 2026-09-29.
