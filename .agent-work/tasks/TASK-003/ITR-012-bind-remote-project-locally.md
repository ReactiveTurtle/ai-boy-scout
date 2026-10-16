# ITR-012 - Bind remote project paths locally

State: verifying (human acceptance pending).

## Result

The shared configuration maps a remote project ID to an existing local path on
one installation. AI consent defaults to false, is revoked when the path
changes, and is not shared with other machines. Changing the remote database
clears stale local bindings.

## Verification

Focused config tests and 209 Core tests passed. The live config has no remote
bindings because no Firebase connection has been entered.
