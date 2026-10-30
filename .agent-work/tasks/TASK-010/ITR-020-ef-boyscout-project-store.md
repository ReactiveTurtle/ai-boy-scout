---
id: ITR-020
topic: Move BoyScout project catalog operations to EF Infrastructure
type: implementation
state: verifying
---

## Expected result

BoyScout project list/create/AI-access operations go through an Application project-store port implemented with EF Core against the existing version-16 SQLite schema.

## In scope

- Add a BoyScoutDbContext and persistence entities for projects, project paths, reports (read projection), and AI access.
- Add an Application `IProjectStore` port and an EF implementation.
- Route `/api/projects`, project creation, AI access, and setup project checks through the port.
- Preserve Git repository identity aliasing, primary-path rules, counts, activity, and AI consent.
- Add isolated SQLite integration tests using the existing legacy migrator.

## Out of scope

- Rewriting ReportStore/TaskStore database operations in this iteration.
- Removing the legacy schema migrator before EF migrations cover all existing versions.
- Enabling Firebase as the selected storage backend.

## Primary risk

The existing API report/task adapters will continue to share the same SQLite file while project writes move to EF; mapping and transactions must remain compatible.

## Verification

- EF project-store tests against a temporary database migrated with the existing schema.
- Full BoyScout test suite and API/MCP build to temporary output.
- Runtime checks must not stop the active process on port 5187.

## Verification result

- `dotnet test tests\BoyScout.Core.Tests\BoyScout.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~EfProjectStorePreservesGitIdentityPrimaryPathReportCountsAndAiConsent" -p:OutDir="C:\Users\DENIS~1.AKT\AppData\Local\Temp\opencode\BoyScout-ITR020-Focused\"`: passed 1/1.
- Full `BoyScout.Core.Tests` to `C:\Users\DENIS~1.AKT\AppData\Local\Temp\opencode\BoyScout-ITR020-Full\`: passed 366/366.
- BoyScout.Mcp build: passed with 0 warnings/errors to temporary output.
- Temporary operational API scenario on port 5197 with an isolated SQLite file: EF startup completed, `POST /api/projects` returned 201, `GET /api/projects` returned the created project, and AI access changed to true. The temporary process was stopped; active port 5187 was untouched.

## Follow-up found during verification

- `ProjectStore.ResolveOrCreateProjectAsync` and repository `FindByPathAsync` compile, but the service operation is not part of `IProjectStore`, is not called by setup confirmation, and is not exercised by the focused test. Track setup confirmation idempotency separately in ITR-021; do not treat that path as verified here.
