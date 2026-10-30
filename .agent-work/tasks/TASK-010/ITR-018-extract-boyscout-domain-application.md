---
id: ITR-018
topic: Extract BoyScout Domain and Application contracts from the legacy Core project
type: refactoring
state: verifying
---

## Expected result

BoyScout Domain and Application compile as independent projects; the existing Core implementation references those inner layers and continues to satisfy the same API/MCP behavior.

## In scope

- Move report/task domain models and status rules into `BoyScout.Domain`.
- Move report/task store ports and request/read models into `BoyScout.Application`.
- Make the existing persistence implementation depend inward on Domain/Application/Common without changing its behavior.
- Keep Firebase transport/provider code out of Domain/Application.

## Out of scope

- Converting BoyScout SQLite persistence to EF in this refactor iteration.
- Activating Firebase as the selected report/task store.
- Changing public JSON/API behavior or the TodoGraph publication contract.
- Removing the legacy Core project before its implementations are replaced by Infrastructure adapters.

## Primary risk

BoyScout.Api, BoyScout.Mcp, Firebase adapters, and 365 existing tests share the current Core namespaces; project separation must preserve behavior and contracts while moving types.

## Verification

Full `BoyScout.Core.Tests` suite, plus builds of BoyScout.Api and BoyScout.Mcp using temporary output while active services may hold normal host outputs.

## Verification result

- Full `BoyScout.Core.Tests`: 365/365 passed using temporary output after moving report/task domain types and storage ports into Domain/Application projects.
- `BoyScout.Mcp` build: 0 warnings/errors using temporary output.
- BoyScout.Api compiled as a test-project reference with 0 errors.
- The legacy Core implementation still owns SQLite/Firebase files at this phase; moving persistence into EF Infrastructure is the next iteration.
