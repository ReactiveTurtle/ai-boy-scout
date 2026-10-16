# ITR-017 - Separate consequences from the task description

State: verifying (human acceptance pending).

## Result

The v12 SQLite migration adds a nullable current-consequences field to
proposals and tasks. New current agent verdicts require an independent,
evidence-based Russian consequences statement. Obsolete/unverified verdicts
require null. Old tasks remain valid without the field. The site displays the
statement separately in the draft and confirmed task.

## Verification

236 .NET tests, the MCP smoke test, and Angular production build passed.
Before publishing v12, a SQLite v11 backup was created; migration verification
found 178 immutable report records unchanged and no orphan or foreign-key errors.
The published MCP reports connected. Firebase remains disabled.
