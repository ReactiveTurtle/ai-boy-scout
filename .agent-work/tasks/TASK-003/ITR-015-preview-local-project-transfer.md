# ITR-015 - Preview a local project transfer

State: verifying (human acceptance pending).

## Result

The site previews the full local v11 project graph as counts and a checksum.
The reader uses a consistent SQLite read-only transaction and rejects broken
links, cross-project associations and unsupported schema. No import or delete
endpoint is offered, so confirmation cannot accidentally discard local data.

## Verification

Snapshot tests for empty, linked, and malformed projects passed; 215 Core
tests passed and Angular built. A live Firebase migration has not been tested.
