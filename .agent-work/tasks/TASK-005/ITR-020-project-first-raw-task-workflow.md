# ITR-020 - Project-first raw-task workflow

**Type:** implementation  
**State:** verifying  

## Expected user flow

- `/` opens the Projects list; the current candidate inbox remains reachable as Candidates.
- Opening a project shows only its task list and its raw-item queue.
- A user submits one free-text raw description inside the project. Its source is `user`; imported Boy Scout candidates use source `boyscout`.
- The raw item stays in the project queue while the agent asks clarifying questions. Answers trigger another analysis round.
- Only after no questions remain and a non-empty acceptance-criteria set exists does the agent finalize title/description/criteria and automatically create a linked TaskNode in the project graph.
- The generated task still requires codebase preflight and explicit `К разработке` approval before implementation.
- Task-to-task relationships can cross project boundaries; project task lists remain project-scoped and the global graph displays cross-project relations.

## Boundary scenarios

- Empty raw text is rejected; title and acceptance criteria are not requested from the user.
- An agent response with pending questions or missing criteria creates no TaskNode and cannot start code work.
- Clarification can require multiple rounds; all prior answers remain visible to the agent.
- User-created and Boy Scout-created raw items remain distinguishable in queue, graph, and history.
- Imported projects without a local path can still show their queue/tasks; repository-dependent preflight/development stays blocked until a local path is attached.
- Existing task-plan suggestions that attach to or update an existing task retain human confirmation; only a new, clarified task is automatically materialized.
- Cross-project relations reject self-links, missing endpoints, invalid relation types, and duplicate links; filtering a project must still indicate external related tasks.

## Verification

- Migrate and test source attribution, raw-answer persistence, multi-round questions, criteria gate, automatic TaskNode creation, and explicit development approval.
- Test project-scoped list/create plus global cross-project link visibility.
- Exercise `/` -> project -> raw submission -> agent clarification -> generated task in graph in a browser using an isolated database.

## Verification progress

- Projects is now the default route; `/index.html` remains the separate Boy Scout inbox.
- The project workspace loads only that project's tasks and pending raw candidates, and creates user raw items from a single free-text field.
- Candidate records carry a `user` or `boyscout` source; clarification rounds and answers persist. Final task title/description/criteria are generated after questions; only a valid criteria-bearing `create_task` is auto-materialized and linked to its source candidate.
- `К разработке` remains a separate explicit preflight approval; approval is blocked while criteria are placeholders.
- Added cross-project task-link API and UI; project task lists remain scoped and show related task/project names.
- Full solution tests pass: **355/355**. Browser checked `/` → Projects → project workspace, scoped tasks/queue, a single raw text field, no title/criteria fields, and the global graph link.
- An isolated browser project-creation/raw-submission flow and a live external OpenCode/GitLab run have not yet been exercised. External provider MCPs are disabled in the service settings.
