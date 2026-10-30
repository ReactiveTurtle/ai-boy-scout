---
id: ITR-013
topic: Add a typed Common contract between BoyScout and TodoGraph
type: implementation
state: accepted
---

## Expected result

BoyScout publishes an explicit, versionable TodoGraph intake contract from `Common`; TodoGraph maps that transport contract into its own application/domain input without sharing entities.

## In scope

- Add a dependency-free Common project with report/task publication DTOs.
- Publish `sourceNamespaceId` separately from the report-origin installation metadata.
- Map the contract at the TodoGraph HTTP boundary into TodoGraph-owned models.
- Extend the focused publication test to deserialize and verify the contract.

## Out of scope

- Moving Firebase or EF types into Common.
- Sharing BoyScout/TodoGraph domain entities.
- Reworking existing database persistence or process workflows.

## Primary risk

Common becoming a shared-domain dumping ground; it must contain only the cross-module wire contract in this iteration.

## Verification

Focused publisher contract test and builds of BoyScout Core and TodoGraph using temporary output while the live TodoGraph host holds its normal assembly.

## Result

- The publisher contract test deserializes the outbound request as `ApprovedReportPublication` and verifies `SourceNamespaceId` separately from `SourceReportId`.
- The focused test passed; it built Common, BoyScout Core/API, TodoGraph.Domain, TodoGraph API, and the test assembly to temporary output with 0 build errors.
