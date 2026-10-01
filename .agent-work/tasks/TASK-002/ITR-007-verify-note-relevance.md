# ITR-007 - Verify note relevance before task creation

Status: verifying

## Expected result

OpenCode checks the current project before proposing work: current findings become drafts, obsolete findings are automatically rejected, and inconclusive findings wait for human review as unverified.

## In scope

- read-only project inspection by the triage agent;
- persisted relevance verdict and evidence;
- automatic rejection of obsolete notes without creating tasks;
- explicit `rejected` note status and `unverified` proposal state;
- migration, tests, API, and UI updates.

## Out of scope

- running builds or tests in inspected projects;
- modifying inspected projects;
- automatically creating tasks;
- semantic grouping of multiple notes.

## Primary risk

The agent could mark a real issue obsolete based on weak evidence.

## Decision

Automatic rejection is allowed only for an explicit `obsolete` verdict backed by a stored evidence summary. Missing paths or insufficient evidence produce `unverified` and require human action.

## Human decision

- obsolete notes are automatically moved to `rejected`;
- the agent has read/search access only;
- inconclusive checks are shown as `unverified`.

## Changed

- added schema v4 relevance, evidence, terminal rejection, and persistent automation settings;
- limited the triage agent to read, glob, grep, and list tools;
- added strict `current`, `obsolete`, and `unverified` AI response handling;
- current findings produce reviewable task drafts;
- obsolete findings atomically become dismissed/rejected without a task;
- inconclusive findings remain new and block the queue for review;
- added a persisted start/stop API and UI control that cancels active OpenCode work safely;
- added `Отклонённые` filtering and verification evidence in the UI.

## Verification

- read-only OpenCode smoke test inspected real files and correctly returned `current` with evidence;
- working note `BSR-000001` returned `unverified` when its referenced project could not be inspected sufficiently;
- no task was created and the note was not rejected in the inconclusive case;
- pause persisted across a full API restart, then resume restored enabled processing;
- schema v4 migration preserved all 174 report rows against the backup hash;
- migration verification found 0 orphan reports and 0 foreign-key errors;
- full suite passed 22 Core tests and the MCP protocol smoke test;
- Angular production build, formatting, npm audit, and NuGet vulnerability scan passed;
- published schema v4 MCP connected successfully.

## Backup

`%LOCALAPPDATA%\boy-scout-mcp\backups\reports-2026-09-29T14-26-48.666Z.db`

## Human confirmation

- [ ] The relevance verdicts and evidence are understandable
- [ ] Automatic rejection of obsolete notes is accepted
- [ ] The start/stop control is accepted
