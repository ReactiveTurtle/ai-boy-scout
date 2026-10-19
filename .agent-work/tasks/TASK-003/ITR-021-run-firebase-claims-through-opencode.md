# ITR-021 - Run Firebase claims through OpenCode

## Topic

Process a claimed Firebase note with the existing OpenCode generator and persist its result under a renewed per-note lease.

## Expected result

The production background worker can claim a consented remote note, generate the same three supported verdicts as SQLite, renew its lease during long generation, and refuse to persist if lease renewal is lost.

## In scope

- integrate Firebase claiming into the existing single worker so the global pause/cancel control remains authoritative;
- map a Firebase note/proposal into the existing generator input;
- renew the Firebase project lease throughout generation;
- persist current, obsolete, unverified, and failed outcomes with the current lease handle;
- test mapping and lease-loss/result-fencing behavior.

## Out of scope

- human review endpoints/UI and task confirmation/rejection;
- enabling project Firebase mode or routing new MCP/API notes;
- changing SQLite worker behavior;
- Firebase/MCP browsing or merged pagination.

## Primary risk

OpenCode may run longer than a lease. Cancellation or a failed renewal must prevent result persistence; stale output must never replace a later worker's proposal.

## Verification

Run the targeted worker/Firebase tests, then `dotnet test BoyScout.sln`.

## State

verifying

## Result

- The existing worker continues to claim local SQLite proposals first. With no local proposal, it checks configured Firebase credentials and consented bindings, then claims one eligible note at a time through `FirebaseTaskRepository`.
- Firebase note/proposal data is validated and mapped into `TaskProposal`, preserving remote ID, bound project path, note content, review intent/context, attempt count, proposal timestamps, and review timestamps.
- OpenCode generation renews the Firebase lease every 30 seconds using a two-minute lease. Renewal loss cancels generation and discards its output. Pause/shutdown cancellation leaves the proposal processing; generator errors attempt a fenced failure save.
- Current, obsolete, and unverified outcomes use the repository's fenced save APIs. Firebase remains a worker-only fallback; storage selection and API/MCP routing are unchanged.
- Added focused mapping tests for identity, path, report content, review context, attempts/timestamps, and fail-closed invalid identity/content.

## Changed files

- `src/BoyScout.Api/Ai/ProposalGenerationService.cs`
- `tests/BoyScout.Core.Tests/OpenCodeProposalGeneratorTests.cs`

## Verification

- `dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~OpenCodeProposalGeneratorTests` - passed, 14 tests.
- `dotnet test BoyScout.sln` - passed, 260 tests.

## Residual risk

- Firebase claim, lease-renewal-loss, and remote outcome dispatch are integrated in the hosted worker but do not yet have an end-to-end worker test; repository fencing behavior is covered separately by `FirebaseTaskRepositoryTests`.
