# Boy Scout

Local engineering notes, AI-assisted task preparation, and MCP integration.

The source code is independent from application projects. Runtime data remains in:

```text
%LOCALAPPDATA%\boy-scout-mcp\reports.db
```

## Commands

```text
npm install
npm --prefix web-ui install
npm test
npm run start:mcp
npm run test:mcp
npm run publish:ui
npm run start:ui
dotnet test BoyScout.sln
```

`publish:ui` builds Angular and publishes the ASP.NET Core host to `artifacts/api`. After publishing, `start:ui` serves the report browser and its API at `http://127.0.0.1:5187`.

For Angular development, start the API with `dotnet run --project src/BoyScout.Api --urls http://127.0.0.1:5187`, then run `npm start` from `web-ui`. The Angular development server uses the included proxy configuration and opens at `http://localhost:4200`.

The current release preserves the original four MCP tools:

- `boy_scout_add`
- `boy_scout_list`
- `boy_scout_get`
- `boy_scout_update_status`

## Notes and tasks

Open `http://127.0.0.1:5187` after publishing and starting the UI. The Russian interface keeps raw notes separate from implementation-ready tasks:

1. MCP records a raw note.
2. A read-only OpenCode agent checks whether the finding still exists in the current project.
3. A current finding recommends a Russian task draft; an obsolete finding recommends rejection with a separate Russian final reason; an inconclusive check remains unverified.
4. Every recommendation waits for human review while the worker continues with other eligible notes.
5. Human confirmation either creates a `todo` task and resolves the note or rejects the proposal and dismisses the note.

A user may also submit a specific rejection reason with `POST /api/notes/{id}/rejection-review` and body `{ "reason": "..." }`. The request requires existing per-project AI access, returns a proposal in `queued` state, and does not grant consent. OpenCode checks that exact reason after claiming it. An `obsolete` verdict remains `ready` until `POST /api/task-proposals/{id}/confirm-rejection` confirms it; `current` recommends task creation and `unverified` has no recommendation.

A reviewer can return a `ready`, `unverified`, or `failed` proposal with `POST /api/notes/{id}/clarification` and body `{ "instruction": "..." }`. The trimmed instruction is limited to 2,000 characters, requires existing project AI access, clears the previous generated result, and queues the same proposal for an independent check that addresses the clarification.

The note workflow API exposes the user-facing review states `queued`, `checking`, `awaiting_decision`, `processed`, and `rejected`. Use `POST /api/notes/workflows` with `{ "ids": ["BSR-000001"] }` for up to 100 notes, `GET /api/notes/{id}/proposal` for the full proposal (`204` means the note exists without one, `404` means the note is missing), and `POST /api/notes/{id}/restore` to reopen a dismissed note whose proposal was rejected.

`POST /api/notes/{id}/reprocess` reopens a resolved note, deletes its exclusive linked task and link, and returns `{ workflow, proposal, deletedTaskId }` with the clean proposal queued again. It returns `409` unless the note has exactly one linked task, that task has exactly one source note, and any existing proposal is confirmed. Reprocessing does not grant project AI access, so the worker skips the queued proposal until access already exists or is granted separately.

Task creation is never automatic. Only one consented proposal is processed at a time; ready proposals remain available for human review without blocking the worker.

`POST /api/task-proposals/{id}/requeue` cancels the active generation after atomically returning its `processing` proposal to `queued`. Both proposal confirmation endpoints and requeue return `409` when the proposal is missing or in an incompatible state. Proposal responses expose `recommendedAction`, `finalRejectionReason`, Russian localized report fields, the submitted rejection reason, and independent verification separately.

Background processing is enabled by default and can be paused or resumed from the UI. The setting persists in SQLite. Set `BOY_SCOUT_AI_ENABLED=false` to hard-disable it, `BOY_SCOUT_AI_TIMEOUT_SECONDS` to change the 180-second timeout, or `BOY_SCOUT_OPENCODE_COMMAND` and `BOY_SCOUT_OPENCODE_CONFIG` to override the CLI integration.

Each background verification uses the session ID emitted by its own `opencode run --format json` events and then calls `opencode session delete <sessionID>` with the same command, config, and working directory. Cleanup has its own 15-second timeout and still runs after invalid output, a nonzero exit, timeout, or cancellation. A successful verification fails if its session cannot be identified or deleted; an earlier generation failure remains primary and cleanup failure is logged. The worker never uses the session list or a latest-session heuristic for deletion.

Reports remain available through OpenCode as well, for example:

```text
Show the latest Boy Scout Reports.
Show open Boy Scout Reports for the current project.
Open BSR-000171.
```

The database can also be opened with a SQLite client at `%LOCALAPPDATA%\boy-scout-mcp\reports.db`.

## Structure

- `src/BoyScout.Core` contains the new shared .NET storage and migration code.
- `src/BoyScout.Mcp` contains the active .NET stdio MCP server.
- `src/BoyScout.Api` contains the local HTTP API, OpenCode worker, and production UI host.
- `web-ui` contains the Angular notes-and-tasks desk.
- `.opencode/opencode.triage.json` defines the read-only relevance and task-draft agent.
- `opencode/skills` contains the globally registered Boy Scout skill.

Schema version 9 preserves schema v8 data and adds explicit recommendations, final rejection reasons, and localized report display fields to task proposals. Legacy ready/current drafts become `create_task` recommendations; legacy rejected/obsolete rows become `reject` recommendations and reuse stored verification or the submitted reason when available. Original report text is never rewritten. Explicit per-project consent remains required before a queued proposal can be claimed, and migration grants no projects automatically. Before a production migration, run `npm run backup:db`. Publish server changes with `npm run publish:mcp` before restarting OpenCode.
