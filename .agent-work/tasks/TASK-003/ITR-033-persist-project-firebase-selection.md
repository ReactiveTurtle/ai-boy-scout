# ITR-033 - Persist per-project Firebase selection

## Topic

Persist whether a bound remote project receives new notes from the corresponding local path.

## Expected result

The local config stores a default-off Firebase write flag per remote-project binding; enabling is refused without credentials, a valid local directory, and AI-read consent.

## In scope

- add an optional default-false flag to `ProjectBinding` and config persistence;
- preserve the flag when rebinding the same path; reset it when the path changes;
- add enable/disable operation with fail-closed prerequisites;
- test old config compatibility, default-off, enable/disable, credentials/consent/path checks and rebind reset.

## Out of scope

- API/UI activation wizard;
- routing report writes to Firebase;
- enabling any project as part of this iteration.

## Primary risk

Existing installation config must continue to load as SQLite/default-off, and credentials must never be moved into project repositories.

## Verification

`dotnet test tests/BoyScout.Core.Tests/BoyScout.Core.Tests.csproj --filter FullyQualifiedName~BoyScoutConfigServiceTests` passed (15 tests). `git diff --check` passed.

## Result

`ProjectBinding` now persists `FirebaseWriteEnabled`, defaulting false for legacy configs. `SetFirebaseWriteEnabled` requires a saved Firebase connection, a bound path, and existing AI-read consent to enable; disabling remains available if the path later disappears. Rebinding the same path preserves the flag; changing the path resets both consent and Firebase writes. No project was enabled by this change.

## State

verifying; human acceptance pending
