---
id: ITR-010
topic: Establish the TodoGraph behavior baseline before the EF refactor
type: verification
state: verifying
---

## Expected result

The existing TodoGraph store/workflow suite passes with the two new namespace/legacy migration scenarios before persistence is moved to EF Core.

## In scope

- Run all `TodoGraphCandidateStoreTests` using a temporary output directory to avoid the active process lock on port 5207.
- Record failures without changing workflow behavior; fix only test fixture assumptions introduced by the current draft.

## Out of scope

- EF Core or solution restructuring.
- Changes to the live TodoGraph service/process.
- Changes to unrelated BoyScout/Firebase work.

## Primary risk

The in-progress ADO.NET draft has not yet passed the full existing workflow suite; branch, worktree, rebase, and merge-request behavior must be pinned before structural refactoring.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --no-restore --filter FullyQualifiedName~TodoGraphCandidateStoreTests -p:OutDir=<approved-temp-dir>`.

## Result

The suite passed 64/64 tests, including both new namespace and legacy-migration scenarios. Output was directed to the approved temporary directory because the active TodoGraph process holds the normal `TodoGraph.dll` output.
