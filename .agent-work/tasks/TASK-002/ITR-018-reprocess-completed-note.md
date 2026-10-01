# ITR-018 - Reprocess a completed note

Status: verifying

## Expected result

A completed note can be returned to the processing queue after explicitly deleting the task created from it.

## In scope

- atomically remove the report-task link and its generated task;
- reopen the report and queue its proposal from a clean decision state;
- create a queued proposal for compatible legacy completed notes if needed;
- expose the destructive transition through the backend API.

## Out of scope

- retaining task history after deletion;
- reopening completed work tasks independently;
- affecting tasks linked to other notes;
- UI changes and runtime verification.

## Primary risk

Deleting a task shared by multiple notes could corrupt other note workflows.

## Decision

Reprocessing is allowed only when the linked task belongs exclusively to the selected note. The entire transition is atomic.

## Verification

- completed note, exclusive task, and link become open, deleted, and queued;
- shared-task and invalid-state cases are rejected without partial changes;
- queue processing respects project consent;
- legacy resolved notes without proposals receive a clean queued proposal;
- Core release tests and formatting pass.

## Verification evidence

- `dotnet test BoyScout.sln --configuration Release`: 83 passed;
- `dotnet format BoyScout.sln --verify-no-changes`: passed;
- MCP smoke and npm tests are unchanged because the MCP and UI contracts were not modified.
