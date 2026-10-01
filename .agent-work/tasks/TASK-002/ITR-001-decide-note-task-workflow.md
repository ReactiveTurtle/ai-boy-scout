# ITR-001 - Decide the note-to-task workflow

Status: accepted

## Expected result

The product has an explicit boundary between raw notes, AI proposals, and confirmed implementation tasks.

## Decision

- existing reports are raw notes;
- OpenCode prepares a draft in the background;
- the user confirms a draft before task creation;
- confirming creates a `todo` task and marks the note processed;
- note statuses shown in the product are `new` and `processed`;
- task statuses are `todo` and `done`;
- the historical MCP report statuses remain unchanged for compatibility.

## Human confirmation

Accepted through the user's selection of OpenCode, confirmation before creation, and simplified statuses on 2026-09-29.
