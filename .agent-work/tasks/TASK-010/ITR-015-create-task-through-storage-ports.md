---
id: ITR-015
topic: Create a TodoGraph task through provider-independent Application ports
type: implementation
state: verifying
---

## Expected result

When the raw-request Process reaches its `CreateTask` action, an Application use case creates a domain task through `ITaskRepository`, links the generated task ID back to the ProcessRun, and commits both changes atomically through `IUnitOfWork`.

## In scope

- Add a TodoGraph Task domain entity with title/description/criteria invariants.
- Add Application `ITaskRepository` and `IUnitOfWork` ports.
- Move `SaveChangesAsync` out of the process repository port into the shared TodoGraph unit-of-work port.
- Add the process-to-task creation use case and test it with an in-memory repository adapter (no EF/Firebase dependency in Application).
- Keep Process definition/stage and Task persistence independently substitutable behind ports.

## Out of scope

- Implementing the EF Task repository in this iteration.
- Moving all legacy CandidateStore methods or public API DTOs.
- Angular UI changes or process-definition administration endpoints.

## Primary risk

Task row insertion and ProcessRun linking must be atomic so a failure cannot leave an orphan task or an unlinked process.

## Verification

Application-level test verifies successful link, criteria validation, and no repository write when the process is not awaiting task creation. A fake unit-of-work executes the same transaction boundary without any storage provider.

## Verification result

- `TodoGraph.Application.Tests.TaskCreationServiceTests`: 2/2 passed using only TodoGraph.Application ports and in-memory adapters.
- `TodoGraph.Infrastructure.Tests.ProcessRepositoryTests`: 2/2 passed after moving commit responsibility to `IUnitOfWork`.
- No EF or Firebase dependency was added to TodoGraph.Application.
