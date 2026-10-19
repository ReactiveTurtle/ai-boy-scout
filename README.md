# Boy Scout

Local engineering notes, AI-assisted task preparation, and MCP integration.

The source code is independent from application projects. Runtime data remains in:

```text
%LOCALAPPDATA%\boy-scout-mcp\reports.db
```

## Install and run

Build from a local clone. Required tools are Git, Node.js 22.5 or later, and the .NET 10 SDK. OpenCode CLI (`opencode`) is required for background note verification; the site and MCP can be installed without enabling that worker.

```text
git clone https://github.com/ReactiveTurtle/ai-boy-scout.git
cd ai-boy-scout
npm install
npm --prefix web-ui install
npm run publish:mcp
npm run publish:ui
```

`publish:mcp` creates `artifacts/mcp/BoyScout.Mcp.dll`. `publish:ui` builds Angular and publishes the local ASP.NET Core host to `artifacts/api`. Start the site in the foreground with:

```text
npm run start:ui
```

Open `http://127.0.0.1:5187`. The site and API bind to loopback only. Keep the source checkout in a stable local path: the published app uses that checkout as its content root, and the per-repository MCP setup refers to the published MCP DLL by absolute path.

### Install MCP in a project repository

1. Publish the MCP from the Boy Scout checkout with `npm run publish:mcp`.
2. Start the Boy Scout site and open it in a browser.
3. In Projects, add the target repository as a local project using its existing directory path.
4. Select that project, open its settings, choose **Подключить MCP и ссылку на сайт к репозиторию**, and enter the repository root.
5. Review the preview and confirm it in the UI.
6. Restart OpenCode in that repository so it reloads `opencode.json`.

The setup adds a project-local `boy-scout` stdio MCP entry to `opencode.json`, creates a `boy-scout.cmd` shortcut on Windows (`boy-scout.sh` on Unix-like systems), and excludes those local setup files through `.git/info/exclude`. It does not copy Boy Scout into the target repository, commit configuration, expose credentials, grant AI read consent, or start the site. If Boy Scout MCP is already registered globally in OpenCode, disable the duplicate global entry separately. To remove the project installation, remove its `boy-scout` entry and shortcut; `.git/info/exclude` can be edited manually.

### Run the site in the background on Windows

The background worker is hosted by the Boy Scout API process. Run that API at user logon with Task Scheduler; do not register the stdio MCP server as a background task because OpenCode starts it when needed. Run the following in PowerShell from the Boy Scout checkout after publishing:

```powershell
$root = (Resolve-Path .).Path
$action = New-ScheduledTaskAction `
  -Execute "dotnet" `
  -Argument "`"$root\artifacts\api\BoyScout.Api.dll`" --contentRoot `"$root`" --urls http://127.0.0.1:5187" `
  -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName "BoyScout" -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description "Local Boy Scout site and background worker"
Start-ScheduledTask -TaskName "BoyScout"
```

The task runs as the current user after sign-in, uses that user's local database/configuration, and restarts after a process failure. Visit `http://127.0.0.1:5187` to verify it started. Stop it with `Stop-ScheduledTask -TaskName "BoyScout"`; remove automatic startup with `Unregister-ScheduledTask -TaskName "BoyScout" -Confirm:$false`. Do not run both the scheduled task and `npm run start:ui` at the same time because both need port 5187.

For a foreground/development run, use the API and Angular dev server instead:

```text
dotnet run --project src/BoyScout.Api --urls http://127.0.0.1:5187
```

In another terminal, from `web-ui`, run `npm start`; the dev UI is at `http://localhost:4200` and proxies API requests to the local host.

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

The HTTP API is intended for localhost only. Non-loopback API requests are rejected; browser writes from unrelated origins are rejected as well. Do not expose the site through a public reverse proxy.

The current release preserves the original four MCP tools:

- `boy_scout_add`
- `boy_scout_list`
- `boy_scout_get`
- `boy_scout_update_status`

## Notes and tasks

Open `http://127.0.0.1:5187` after publishing and starting the UI. The Russian interface keeps raw notes separate from implementation-ready tasks:

The Projects sidebar can create a local project by name and an existing directory path before it has any notes. A path already linked to another project cannot be added again. `POST /api/projects` accepts `{ "name": "...", "path": "..." }`. `GET /api/tasks` returns cursor pages (`items`, `total`, `nextBefore`) with optional `projectId`, `status`, `query`, `before`, and `limit` (1-100); the task list requests more pages only on demand.

"Настроить репозиторий" previews and confirms a local OpenCode MCP configuration and a shortcut to the common site in a chosen Git repository. It records the project in SQLite, adds the setup files to that checkout's `.git/info/exclude`, and does not copy the site or put secrets in the repository. Publish the MCP with `npm run publish:mcp` first and restart OpenCode after installation; an existing global MCP registration must be disabled separately if it duplicates the project entry.

"Хранилище и имя" sets the installation name used on new notes and on human-confirmed tasks. The Firebase form stores service-account credentials in the per-user local config (DPAPI-protected key on Windows) and checks access by listing remote projects. A successful check does not switch any notes or tasks to Firebase. `BOY_SCOUT_CONFIG` can override the config path; with `BOY_SCOUT_DB` set, the default config lives beside that database.

With Firebase access checked, the selected project's settings panel can create and list shared Firebase projects, bind an existing directory on this computer, and grant this installation separate AI read consent. A changed binding revokes that consent. These project setup operations do not migrate local notes or tasks and do not switch the API or MCP away from SQLite. Do not grant AI consent on another computer without checking its local binding.

General BoyScout settings contain the installation name and Firebase credentials. Select a project to open its separate project settings for AI access, repository setup, remote path binding and transfer preview. A queued note without AI read consent explains why it cannot be processed; the global automation toggle does not grant consent. The per-project Firebase choice remains disabled. The intended activation is an explicit check-and-confirm workflow: it will route only new records to Firebase while existing records remain local; manually copying an individual local note is a separate operation.

The panel can preview a local project's complete v11 data graph before a future transfer: report, proposal, task and link counts plus a deterministic snapshot checksum. This preview is read-only; no import or local deletion endpoint exists yet. The Firebase project-root lease and proposal CAS are not connected to the production worker, so distributed processing remains disabled.

1. MCP records a raw note.
2. A read-only OpenCode agent checks whether the finding still exists in the current project.
3. A current finding recommends a Russian task draft; an obsolete finding recommends rejection with a separate Russian final reason; an inconclusive check remains unverified.
4. Every recommendation waits for human review while the worker continues with other eligible notes.
5. Human confirmation either creates a `todo` task and resolves the note or rejects the proposal and dismisses the note.

A user may also submit a specific rejection reason with `POST /api/notes/{id}/rejection-review` and body `{ "reason": "..." }`. The request requires existing per-project AI access, returns a proposal in `queued` state, and does not grant consent. OpenCode checks that exact reason after claiming it. An `obsolete` verdict remains `ready` until `POST /api/task-proposals/{id}/confirm-rejection` confirms it; `current` recommends task creation and `unverified` has no recommendation.

A reviewer can return a `ready`, `unverified`, or `failed` proposal with `POST /api/notes/{id}/clarification` and body `{ "instruction": "..." }`. The trimmed instruction is limited to 2,000 characters, requires existing project AI access, clears the previous generated result, and queues the same proposal for an independent check that addresses the clarification.

Returning a processing proposal to the queue, retrying a ready, failed, or unverified proposal, restoring a rejected note, or reprocessing a completed note requires a non-empty JSON `comment` (maximum 2,000 characters after trimming). The queued proposal carries this comment to the next OpenCode verification. The UI also requires a comment for returning a proposal for clarification.

New task drafts and confirmed tasks have a separate `currentConsequences` field for independently verified consequences of the current behavior. It is distinct from the source note's impact and the proposed implementation description; tasks created before schema v12 keep this field null.

The note workflow API exposes the user-facing review states `queued`, `checking`, `awaiting_decision`, `processed`, and `rejected`. Use `POST /api/notes/workflows` with `{ "ids": ["BSR-000001"] }` for up to 100 notes, `GET /api/notes/{id}/proposal` for the full proposal (`204` means the note exists without one, `404` means the note is missing), and `POST /api/notes/{id}/restore` to reopen a dismissed note whose proposal was rejected.

`POST /api/notes/{id}/reprocess` reopens a resolved note, deletes its exclusive linked task and link, and returns `{ workflow, proposal, deletedTaskId }` with the clean proposal queued again. It returns `409` unless the note has exactly one linked task, that task has exactly one source note, and any existing proposal is confirmed. Reprocessing does not grant project AI access, so the worker skips the queued proposal until access already exists or is granted separately.

Task creation is never automatic. Only one consented proposal is processed at a time; ready proposals remain available for human review without blocking the worker.

`POST /api/task-proposals/{id}/requeue` cancels the active generation after atomically returning its `processing` proposal to `queued`. Both proposal confirmation endpoints and requeue return `409` when the proposal is missing or in an incompatible state. Proposal responses expose `recommendedAction`, `finalRejectionReason`, Russian localized report fields, the submitted rejection reason, and independent verification separately.

Background processing is enabled by default and can be paused or resumed from the UI. The setting persists in SQLite. Set `BOY_SCOUT_AI_ENABLED=false` to hard-disable it, `BOY_SCOUT_AI_TIMEOUT_SECONDS` to change the 180-second timeout, or `BOY_SCOUT_OPENCODE_COMMAND` and `BOY_SCOUT_OPENCODE_CONFIG` to override the CLI integration.

Each background verification runs `opencode run --title "BoyScout: verification BSR-..." --format json`. Its session is retained for inspection, including after an invalid result, failure, timeout, or cancellation. The worker does not delete or guess session IDs.

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

Schema version 12 preserves v11 data and adds optional current-consequence fields to proposals and tasks. Legacy rows remain without inferred consequences, authors or decision times. Configure the local BoyScout name and Firebase service-account fields in the site; the latter are stored on this computer but remote mode is not enabled yet. Explicit per-project consent remains required before a queued proposal can be claimed. Before a production migration, run `npm run backup:db`. Publish server changes with `npm run publish:mcp` before restarting OpenCode.
