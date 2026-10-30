# ITR-002 - Store MR link and Code Review state

**Type:** implementation  
**State:** ready

## Parent task

TASK-004 - Background MR comment monitoring.

## Topic

Allow a user to set a Todo Graph task's MR URL and move it into `Code Review`, persisting both values in the task node.

## Expected result

After this iteration, the graph inspector can save a valid MR URL and task status; a task is eligible for later background polling only when status is `code_review` and a URL exists. Confirm with a store/API test and an operational UI/API check.

## In scope

- Persist an optional MR URL on task nodes with a migration for existing SQLite databases.
- Expose a task update endpoint that accepts task status and MR URL.
- Validate supported statuses and absolute HTTP(S) URL input.
- Add graph inspector controls to update status and URL.
- Preserve the existing imported-task rule: a user edit marks the Todo Graph copy user-modified.

## Out of scope

- MCP server configuration/client, background polling, comment storage, read marks, or reply posting.
- Creating manual tasks or changing Boy Scout task data.

## Boundary scenarios

- Status `code_review` plus an MR URL is eligible for polling by a later iteration.
- Status `code_review` with no URL is saved but is not pollable.
- Any non-`code_review` status is not pollable even if a URL is retained.
- Invalid or non-HTTP(S) URLs and unsupported statuses are rejected without changing task data.
- Existing imported tasks retain their current fields after startup migration.

## Patch and verification

- Added `merge_request_url` storage/migration, task status update API, and controls in the graph inspector.
- Added store tests for valid updates, eligibility data, unsupported status, and unsafe URLs.
- Targeted tests passed: 4/4 using an isolated `BaseOutputPath` because the running Todo Graph instance locks its default DLL output.
- Operational check on an isolated Todo Graph instance: imported a task, PATCHed it to `code_review` with an HTTPS GitLab MR URL, confirmed the returned task and `/api/graph` expose both values, and confirmed `/graph.html` returns HTTP 200.
- The inspector was served and its API path exercised; browser interaction was not verified.

## Not checked

- Actual background polling, GitLab MCP connectivity, comment visibility/marking, or replies; these are subsequent iterations.
