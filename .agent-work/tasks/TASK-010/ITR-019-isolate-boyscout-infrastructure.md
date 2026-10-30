---
id: ITR-019
topic: Move BoyScout persistence and Firebase adapters behind Infrastructure
type: refactoring
state: verifying
---

## Expected result

BoyScout.Api/Mcp depend on Domain/Application/Infrastructure; SQLite storage, configuration, Firebase adapters, and outbound TodoGraph publishing live in `BoyScout.Infrastructure`. The legacy Core project becomes a compatibility facade without data access.

## In scope

- Move existing BoyScout database, ReportStore, TaskStore, AutomationStore, configuration, Firebase, and publication adapter source into Infrastructure.
- Keep Domain/Application free of Sqlite, EF, Firebase, and HTTP dependencies.
- Move `IAutomationStore` to Application and update API/MCP/test project references/namespaces.
- Add EF Core SQLite dependencies to Infrastructure as the persistence provider used by the next migration iteration.
- Preserve all current HTTP, MCP, local SQLite, Firebase adapter, and publisher behavior.

## Out of scope

- Replacing legacy SQLite data operations/migrations with EF queries; handled in the next iteration.
- Switching runtime backend to Firebase or changing the default selected store.
- Renaming public JSON fields/routes.

## Primary risk

This is a broad namespace/project move across API, MCP, and tests; the pre-change baseline was 365/365 `BoyScout.Core.Tests`.

## Verification

- Full 365-test suite using temporary output.
- BoyScout.Api and BoyScout.Mcp builds using temporary output while ports 5187/5207 remain active.

## Verification result

- Full `BoyScout.Core.Tests`: 365/365 passed after moving report/task domain models and ports and moving storage/Firebase implementations into Infrastructure.
- BoyScout.Api compiled as a test-project dependency with 0 errors.
- BoyScout.Mcp build: 0 warnings/errors to temporary output.
- The Core project is now a compatibility facade; Infrastructure still contains legacy ADO persistence to be replaced with EF in a later iteration.
