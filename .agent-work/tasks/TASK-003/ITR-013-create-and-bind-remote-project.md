# ITR-013 - Create and bind remote projects

State: verifying (human acceptance pending).

## Result

The localhost site can list and create remote Firebase projects and bind a
local path and separate AI consent. The project list reads shallow IDs and
metadata instead of the project note/task trees. Saving credentials does not
activate Firebase notes, tasks, the worker, or MCP.

## Verification

Fake HTTP catalog tests and 209 Core tests passed; Angular production build
passed. Published API returned 200 for health/bindings, and 409 for remote
projects before configuring credentials. No live Firebase account is available
to prove connectivity.
