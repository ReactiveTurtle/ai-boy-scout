# ITR-017 - Attach a human-confirmed agent decision

Status: verifying

## Expected result

Every completed relevance check contains an explicit agent recommendation to create a task or reject the note, with human confirmation required for either terminal transition.

## In scope

- persist `create_task` or `reject` as the agent recommendation;
- persist a concise final rejection reason separately from technical evidence;
- keep obsolete notes at human review until rejection is confirmed;
- expose an explicit rejection-confirmation endpoint;
- localize the reviewed note title and explanatory text to Russian while preserving original source fields;
- require Russian task drafts, recommendations, and rejection reasons.
- allow a human to cancel the active agent check and return it to the queue.

## Out of scope

- bulk translating notes before their turn in the queue;
- automatically applying an agent recommendation;
- changing project permissions.

## Primary risk

Legacy assumptions may treat `obsolete` as an immediate terminal transition instead of a recommendation.

## Decision

The agent always checks relevance first. `current` recommends task creation, `obsolete` recommends rejection, and `unverified` provides evidence without a terminal recommendation. Both actionable recommendations wait for a human.

## Verification

- current checks produce a Russian task recommendation;
- obsolete checks produce a Russian final rejection reason and remain at human review;
- only explicit human confirmation dismisses an obsolete report;
- final rejection details show the submitted reason, agent reason, and evidence separately;
- original report text remains stored unchanged.
- cancelling an active check terminates OpenCode and restores the proposal to `queued`.

## Implementation evidence

- schema v9 stores conditional recommendations, final rejection reasons, and localized report display fields without changing `reports` source text;
- `current`, `obsolete`, and `unverified` generation results persist as `ready/create_task`, `ready/reject`, and `unverified/null` respectively;
- `POST /api/task-proposals/{id}/confirm-rejection` is the only agent-workflow transition that both rejects the proposal and dismisses an obsolete report;
- `POST /api/task-proposals/{id}/requeue` changes only `processing` to `queued` before cancelling generation, so stale saves fail their state guard;
- report browse/get queries display localized text through `COALESCE` and search both localized and original text;
- the Angular review card always shows the agent decision and keeps task creation, rejection confirmation, clarification, and user-proposed rejection reasons as distinct actions;
- an active check can be returned to the queue from the checking card without changing automation or project access;
- rejected-note details prioritize the final agent rejection reason, then the submitted user reason, then verification evidence, with an explicit legacy fallback;
- release tests and MCP smoke are the final verification commands; human acceptance remains pending.

## Verification evidence

- `dotnet test BoyScout.sln --configuration Release --no-restore`: 77 passed;
- `npm test`: 77 tests passed, solution build passed with zero warnings/errors, .NET MCP smoke passed;
- `dotnet format BoyScout.sln --verify-no-changes --no-restore`: passed;
- triage JSON and migration/MCP scripts passed local syntax validation;
- `npm run build` in `web-ui`: passed without warnings; Angular template and TypeScript checks completed;
- runtime database and HTTP API were not started or accessed.
