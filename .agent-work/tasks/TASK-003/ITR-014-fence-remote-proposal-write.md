# ITR-014 - Fence remote proposal writes

State: verifying (human acceptance pending).

## Result

An active note can be leased to a named installation. Renewal and manual force
release compare-and-set the project root. Saving a ready proposal requires
the current lease token and generation in the same conditional write as the
proposal, so an intervening release invalidates a stale worker.

## Risk

The current standalone store is not wired to the API worker, and a server-date
checked at GET time does not prove the lease remained unexpired during network
transit to PUT. Other workflow transitions still need protection and tests.

## Verification

Fake HTTP contention tests and 209 Core tests passed. No live Firebase or
emulator verification was performed.
