# ITR-022 - Confirm Firebase rejection

## Topic

Atomically apply a human decision that confirms an obsolete/reject Firebase proposal.

## Expected result

Only an active note with a ready obsolete/reject proposal can be rejected. One project-root CAS updates proposal state/timestamps and note status/timestamp together; duplicate or incompatible decisions are refused.

## In scope

- add `ConfirmRejectionAsync` to `FirebaseTaskRepository`;
- preserve the proposal's final reason and unrelated root data;
- test valid confirmation, invalid/current proposal refusal, duplicate confirmation, and ETag conflict retry.

## Out of scope

- API/UI endpoint wiring;
- proposal retry, restore, clarification or reprocess transitions;
- project activation or Firebase browse integration.

## Primary risk

Proposal and note rejection state must not diverge if multiple readers/decision makers race. Validate and mutate both in one ETag/CAS write.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~FirebaseTaskRepositoryTests` passed (25 tests). `git diff --check` passed.

## Result

`ConfirmRejectionAsync` accepts only an active note with a ready obsolete/reject proposal. A single project-root ETag/CAS write transitions the proposal to rejected, records one `HumanRejectedAt`/`UpdatedAt`, clears approval/consequences, and dismisses the note with the same timestamp. Final reason, evidence, tasks, and unknown fields are preserved. Invalid and duplicate decisions write nothing; precondition conflicts retry.

## State

verifying; human acceptance pending
