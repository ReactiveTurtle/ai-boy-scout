# ITR-002 - Add the task pipeline schema

Status: verifying

## Expected result

The database can store AI proposals, confirmed tasks, and source-note links without changing the existing reports table or MCP contract.

## In scope

- additive schema version 3;
- task proposal, task, and report-task link tables;
- atomic proposal confirmation;
- minimal task status updates;
- SQLite integration tests.

## Out of scope

- OpenCode invocation;
- HTTP API;
- UI changes;
- MCP task tools.

## Primary risk

A partial confirmation could create a task without linking or resolving its source note.

## Decision

Keep proposals and tasks in separate tables and perform confirmation in one immediate SQLite transaction.

## Changed

- added migration `task_pipeline` and schema validation;
- added task proposal and task domain models;
- added claim, save, fail, confirm, list, and status-update operations;
- added migration and lifecycle tests.

## Verification

- Release test suite passed 10 of 10 tests;
- format verification passed;
- tests use temporary databases and do not modify the working database.

## Human confirmation

- [ ] The additive schema is accepted
- [ ] The confirmation transaction is accepted
