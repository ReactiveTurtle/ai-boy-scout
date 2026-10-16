# ITR-006 - Add Firebase transport primitives

State: verifying (human acceptance pending).

## Result

The Core library can authenticate with a service account, conditionally update
one RTDB node using an ETag, allocate numeric IDs, and manage a renewable lease
with an owner and a fencing generation. Tests simulate conflicts and failures.

## Primary risk

Checking a lease and then writing a separate note/proposal node is not atomic.
The backend must update the protected state and fence within one conditional
write. The current primitives are not wired to live workflows.

## Verification

Fake-HTTP-handler tests and 146 Core tests passed. No Firebase emulator or live
service account was available for integration testing.
