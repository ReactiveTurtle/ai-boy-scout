# ITR-007 - Share local configuration between site and MCP

State: verifying (human acceptance pending).

## Result

The site and MCP use the same per-user config for installation identity, name,
and optional Firebase connection fields. The site accepts fields separately
and never returns the private key. SQLite stays selected and both entrypoints
fail closed if Firebase is somehow selected before its full backend exists.

## Primary risk

A saved configuration is not a proven connection: no remote backend is enabled
and no Firebase emulator verification has occurred.

## Verification

Core tests, the MCP smoke test, and Angular build passed.
