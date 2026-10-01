# ITR-003 - Generate task drafts with OpenCode

Status: verifying

## Expected result

An isolated background worker turns an active raw note into a Russian task draft without tools, source changes, or automatic task creation.

## In scope

- dedicated tool-free OpenCode agent;
- one-at-a-time background processing;
- strict JSON response parsing;
- failure persistence and manual retry support;
- safe shutdown and bounded execution time.

## Out of scope

- proposal confirmation API;
- Angular UI;
- automatic task creation;
- source-code edits by the agent.

## Primary risk

Text inside a note could be interpreted as agent instructions or malformed output could be persisted as a task draft.

## Decision

Pass note fields inside a clearly delimited untrusted payload to an agent with every tool denied, then validate the returned JSON before saving it.

## Changed

- added a dedicated tool-free `boy-scout-triage` agent;
- added a sequential hosted service with timeout and cancellation;
- added strict JSONL event and proposal validation;
- added persisted failure handling and a manual retry transition;
- limited the queue to one unfinished proposal at a time.

## Verification

- a real OpenCode CLI smoke test produced a valid Russian proposal from synthetic note data;
- Release build and all 13 Core tests passed;
- format verification passed.

## Human confirmation

- [ ] The tool-free agent boundary is accepted
- [ ] One-at-a-time background processing is accepted
