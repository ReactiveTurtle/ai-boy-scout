# ITR-001 - Record version one

State: verifying (human acceptance pending).

## Result

The previously uncommitted codebase was recorded as the Git root commit `11ccb97` (`Initial Boy Scout version`) before any work on shared storage.

## Verification

Staged only source, docs, tests, and configuration with no known secrets. `git diff --cached --check` passed before committing. Build artifacts, dependencies, and the local SQLite database remained ignored.
