# ITR-008 - Attribute notes and tasks to an installation

State: verifying (human acceptance pending).

## Result

SQLite v10 adds nullable source installation/name to notes and nullable
confirmer installation/name to tasks. New MCP notes and confirmed tasks use the
current saved identity; legacy rows stay valid and have no guessed author.
The site displays both source and confirmer where available.

## Verification

Core migration/attribution tests (146 passed), MCP smoke test, and Angular build.
