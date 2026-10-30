---
id: ITR-006
topic: Add the Common.Domain AggregateRoot base
type: implementation
state: verifying
---

## Expected result

`Common.Domain.AggregateRoot` explicitly represents a DDD aggregate boundary, derives from `BaseEntity`, and `Project` derives from `AggregateRoot`.

## In scope

- Add the abstract AggregateRoot base type without introducing persistence or framework dependencies.
- Make Project inherit from it and cover the relationship with a domain test.

## Out of scope

- Domain events, repository interfaces, other aggregates, and API/persistence behavior.

## Primary risk

Keep the shared abstraction minimal; do not add behavior that has not been requested by a concrete domain scenario.

## Verification

- Focused TodoGraph.Domain.Tests.
- Full solution build to temporary output.

## Verification result

- Project domain tests passed 6/6, including `Project` assignability to `Common.Domain.AggregateRoot`.
- Full `BoyScout.sln` build passed with 0 warnings/errors.
