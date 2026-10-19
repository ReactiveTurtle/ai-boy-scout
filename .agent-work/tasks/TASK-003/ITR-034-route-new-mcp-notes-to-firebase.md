# ITR-034 - Route new MCP notes to Firebase

## Topic

Use per-binding Firebase selection for new IReportStore note writes and remote ID operations.

## Expected result

When a local path is bound and Firebase writes are enabled, MCP/API note additions are written to Firebase. Disabled/unbound projects continue writing SQLite. MCP list/get/status can access enabled remote notes while SQLite history remains intact.

## In scope

- route `ReportStore.Add` through Firebase only for bindings with `FirebaseWriteEnabled`;
- recognize remote note IDs for `Get` and `UpdateStatus`;
- include new Firebase notes in MCP `List` results while retaining local notes;
- add a narrow injectable transport factory/test seam and fake-HTTP ReportStore tests.

## Out of scope

- main Angular/API mixed pagination (`Browse`/`BrowseNotes`);
- task routing (already uses remote proposal confirmations); storage activation UI;
- changing SQLite schema or moving existing notes.

## Primary risk

Do not silently fall back to SQLite when an enabled Firebase write fails; a remote note ID must never be parsed or updated as a local SQLite ID.

## Verification

- `dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseReportStoreTests` passed: 8/8.
- `dotnet test BoyScout.sln` passed: 287/287.
- `git diff --check` passed.

## Result

- Enabled path bindings write to Firebase and return Firebase note data with matching local `ProjectId` when a SQLite path row exists; failures propagate without SQLite fallback.
- Disabled or unbound paths retain SQLite behavior. Remote IDs are validated and require a locally bound remote project; remote notes are included in MCP list results from enabled bindings with path/status/limit filtering.
- No API, MCP, UI, interface, or SQLite schema changes were made. Firebase `Browse`/`BrowseNotes` remain unchanged; Angular still displays Firebase items in the bound project's review panel rather than the main SQLite workspace.

## State

verifying
