---
id: ITR-002
topic: Remove BoyScout publishing and TodoGraph delivery state
type: implementation
state: verifying
---

## Expected result

BoyScout no longer publishes reports to TodoGraph or exposes a TodoGraph-specific report status, and existing BoyScout databases migrate forward while preserving reports and unrelated project/Firebase data.

## Scenarios

1. Updating a report to `accepted` or `planned` changes only BoyScout status and performs no outbound TodoGraph request.
2. BoyScout API/MCP report status filters accept only remaining BoyScout statuses; `sent_to_todo_graph` is no longer exposed.
3. Migrating a v16 BoyScout database to v17 preserves reports, tasks, projects, paths, and Firebase note markers while dropping only the obsolete TodoGraph delivery table.
4. A previously delivered report returns to its stored accepted/planned status after migration; no delivery timestamp or TodoGraph source-status field is exposed.

## In scope

- Remove the publication contract/publisher and MCP dependency.
- Remove delivery-specific statuses, fields, API filters, Angular filter/status styles, and tests.
- Add BoyScout schema v17 to drop TodoGraph delivery tracking while retaining migration history 13/15 for existing databases.
- Remove TodoGraph-specific behavior from migration smoke tests and active documentation.

## Out of scope

- The user-local TodoGraph database outside this repository.
- Historical `.agent-work` records.
- Unrelated BoyScout/Firebase behavior.

## Primary risk

BoyScout migration history 13 and 15 has already shipped. Preserve those migration records/steps and use a forward-only v17 cleanup rather than rewriting historical migrations.

## Verification

- Focused BoyScout test for v16-to-v17 data preservation and delivery-table removal.
- MCP smoke test proving status updates do not publish.
- Full BoyScout tests and Angular production build.

## Verification result

- Focused v16-to-v17 cleanup and legacy v11 migration tests passed 2/2.
- Full `BoyScout.Core.Tests` passed 296/296 using temporary output after adjusting historical-schema validation and downgrade fixtures.
- BoyScout.Mcp built with 0 warnings/errors; `tests/dotnet-mcp-smoke-test.mjs` passed with accepted status unchanged and delivery table absent after migration.
- Angular production build passed; initial bundle is 363.65 kB. The existing `app.scss` budget warning remains (26.97 kB vs 26 kB).
- User-local database outside the repository was not touched; the old host was stopped only after the user's explicit authorization (see ITR-003).
