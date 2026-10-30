# ITR-001 - Discover comment-monitoring boundaries

**Type:** discovery  
**State:** ready

## Topic

Determine the existing integration points and the decisions required before background MR comment polling can be implemented.

## Expected result

Identify the background host, current task/MR support, and unresolved provider/workflow decisions, confirmed by source inspection.

## Verified facts

- `src/BoyScout.Api/Program.cs` hosts `ProposalGenerationService` as a `BackgroundService` only when local AI processing is enabled.
- `README.md` describes Todo Graph as having no repository tools and no MR interaction.
- The task model in `src/BoyScout.Core/Tasks/BoyScoutTask.cs` has no MR URL/provider fields.
- No GitLab, GitHub, merge-request, or pull-request integration symbols were found in source/docs.
- Todo Graph task nodes currently do not represent external MR comments.

## Unknowns

- Which concrete provider MCP server/tools should be used, and how the persistent worker can invoke them.
- Desired polling cadence and UI surface.

## User decisions

- Use MCP for the code-host integration.
- Put the MR URL directly on the task.
- Start polling only while the task status is `Code Review`.
- Persist a local seen/reviewed mark; prepare replies and publish only after human confirmation.

## Architecture boundary discovered

Todo Graph's current background worker polls queued candidate-analysis jobs. Its OpenCode profile (`todo-graph/.opencode/todo-graph.json`) denies all tools, and no code-host MCP server or MR tools are configured in that profile. An MCP server is commonly a stdio child process started by an interactive client; the Todo Graph worker cannot call a tool that exists only inside an unrelated OpenCode session. The user must identify an MCP server and its tools that a persistent worker can invoke before MR polling can be implemented.

## Boundary

This discovery iteration does not implement polling, provider API calls, comment persistence, or replies.

## Topic evolution

The user clarified that the polling trigger is a Todo Graph task in `Code Review`, with the MR URL stored on the task. The user wants an MCP configured by the user, local read marks, and replies that require human approval. The remaining MCP server config is delegated to the user-configurable Todo Graph setup rather than hardcoding a specific server/package.

## Verification

Repository search and source inspection confirmed there is no existing MR integration to extend directly.
