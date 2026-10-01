# ITR-009 - Require explicit project read consent

Status: verifying

## Expected result

OpenCode can process a note only after the user explicitly grants read access to that note's project, confirmed by storage and worker tests.

## In scope

- persist one read-access decision per project;
- expose grant and revoke actions through the API and UI;
- exclude ungranted projects from the worker queue;
- cancel an active generation when access is revoked;
- keep runtime automation paused after deployment.

## Out of scope

- arbitrary filesystem access;
- access outside the selected project root;
- native interactive OpenCode permission prompts;
- granting access automatically from a note path.

## Primary risk

An already claimed proposal could continue reading after access is revoked.

## Decision

Project access is denied by default. Revocation cancels the current generation, while the worker queries only proposals whose project has an explicit persisted grant.

## Verification

- migration preserves existing data and grants no projects;
- worker claim/resume queries ignore ungranted projects;
- API changes and project summaries show the persisted grant;
- revocation cancellation is covered by a focused test;
- `dotnet test BoyScout.sln --configuration Release`: 27 passed;
- `dotnet format BoyScout.sln`: completed with no output;
- `dotnet format BoyScout.sln --verify-no-changes`: passed;
- `node --check scripts/verify-migration.mjs`: passed.
- full `npm test`, including the schema v5 MCP smoke test: passed;
- live migration verification: 177 reports, 19 projects, zero orphans, zero foreign-key errors, and zero granted projects;
- desktop and 500 px mobile screenshots reviewed with the selected-project access control visible.

## Intentionally unchanged

- external OpenCode configuration;
- access outside an explicitly selected project;
- persisted automation remains disabled until the user starts it.
