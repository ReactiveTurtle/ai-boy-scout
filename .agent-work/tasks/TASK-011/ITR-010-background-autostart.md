---
id: ITR-010
topic: Start TodoGraph API and WebApp in the background at Windows logon
type: implementation
state: verifying
---

## Expected result

At the current user's Windows logon, hidden interactive tasks start TodoGraph.Api on loopback port 5207 and TodoGraph.WebApp's Angular dev server on loopback port 4200.

## Scenarios

1. Starting the API task applies EF migrations to the new `todo-graph-v2` database path and serves `/api/health`.
2. Starting the WebApp task serves the Angular scaffold on port 4200.
3. Repeated task starts do not create duplicate service instances.
4. An uninstall script removes only the two TodoGraph scheduled tasks.
5. The existing BoyScout host on port 5187 is left untouched.

## In scope

- Add PowerShell runners and logon-task install/remove scripts.
- Register and start both tasks for the current user.
- Capture service output in the new TodoGraph local data directory.

## Out of scope

- Converting the Angular development server to a production static host.
- Implementing TodoGraph WebApp features or API integration.
- Starting/stopping the BoyScout API or touching its data.

## Primary risks

- Ports 4200/5207 may already be occupied; do not stop unrelated processes.
- The API must keep using `todo-graph-v2`, not the retired user-local TodoGraph database.
- Scheduled tasks and launch scripts must run with the current user, without storing credentials.

## Verification

- Check existing listeners and scheduled-task names before registration.
- Start the scheduled tasks and verify HTTP responses on 4200 and 5207.
- Confirm the tasks are registered for logon and do not change PID 77696 on port 5187.

## Verification result

- Before installation, ports 4200 and 5207 were free, and no `TodoGraph.Api`/`TodoGraph.WebApp` scheduled tasks existed.
- `npm run install:todo-graph-background` registered and started both hidden current-user logon tasks.
- Both tasks report `Running`. Background API `/api/health` returned `ok`; `/api/projects` returned an empty list; SignalR negotiate succeeded. The WebApp returned HTTP 200 on port 4200.
- Task Scheduler reports both tasks as hidden `MSFT_TaskLogonTrigger` tasks for the current user with duplicate starts ignored. Exact process teardown at user logoff was not exercised.
- The Angular WebApp is still the starter page and has no TodoGraph API calls; background hosting works, but UI functionality is not implemented yet.
- BoyScout host PID 77696 on port 5187 was left untouched. The new service uses the separate `todo-graph-v2` data path; the previous user-local database was not opened by this verification.

## Follow-up verification — 2026-10-30

- The registered tasks initially pointed to runner scripts that were absent from the checkout. The remove script also used `String.Contains(string, StringComparison)`, which is unavailable in Windows PowerShell 5.1; the task stopped but matching child processes remained.
- Replaced the process checks with PowerShell 5.1-compatible `IndexOf` checks, waited for matching TodoGraph processes to exit, and restored the API/WebApp runners with output capture to `%LOCALAPPDATA%\todo-graph-v2\logs\api.log` and `webapp.log`.
- Ran the removal command against the existing processes: tasks were unregistered, ports 5207 and 4200 were released, and BoyScout remained on port 5187 with PID 77696 and HTTP 200.
- Ran `npm run install:todo-graph-background` twice, including once while both new tasks were running. Both Release publishes succeeded; each run ended with one API listener and one WebApp listener, and the tasks reported `Running` with logon triggers for the current user.
- The background API returned `{"status":"ok"}` from `/api/health` and HTTP 200 from `/api/projects`; the WebApp returned HTTP 200. API logs confirmed `InitialCreate` and `AddProjectTasks` migrations ran. Both service log files were created and updated.
- During verification, repeated requests to removed graph-era endpoints returned 404 and rapidly increased `api.log`; source of those requests is unknown. Recorded separately as Boy Scout report `BSR-000212`; it was not changed in this iteration.
- PowerShell parser validation passed for all four autostart scripts. User logoff/startup behavior has not been exercised; current tasks are running in the interactive session.
