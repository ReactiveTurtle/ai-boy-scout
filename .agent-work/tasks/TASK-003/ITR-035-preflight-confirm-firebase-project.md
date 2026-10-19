# ITR-035 - Preflight and confirm Firebase project

## Topic

Add a long-running-feeling, inspectable preflight and explicit confirmation API for per-project Firebase writes.

## Expected result

The bound project can be checked for credentials, remote availability, AI consent and worker availability; the confirmation token is revalidated before enabling only new notes. Existing SQLite notes/tasks remain untouched.

## In scope

- add a read-only activation preflight endpoint with local/remote note and task counts;
- return readiness blockers, current automation status and a confirmation token tied to the binding/preflight snapshot;
- add explicit confirm endpoint that recomputes preflight and enables Firebase writes only if token and prerequisites still match;
- no migration, copying or deletion of existing data.

## Out of scope

- Angular wizard controls;
- new-note routing or mixed workspace pagination (implemented in separate iterations);
- auto-granting AI read consent or starting automation.

## Primary risk

Do not enable a remote project against stale binding/count preview or when worker/consent/credentials are unavailable.

## Verification

`dotnet test BoyScout.sln` passed (289 tests). `git diff --check` passed. Config prerequisites are covered by `BoyScoutConfigServiceTests`; the new preflight/confirm minimal API routes are compile-verified but do not yet have endpoint integration tests.

## Result

Added a project-scoped read-only preflight that validates the bound Firebase project, counts local graph data via a read-only snapshot, pages remote note/task counts, reports worker/consent readiness, and returns a confirmation token tied to the local checksum, remote counts and binding. Confirm recomputes the same preflight and only then sets `FirebaseWriteEnabled`; no notes/tasks are moved or deleted.

## State

verifying; human acceptance pending
