---
id: ITR-021
topic: Resolve setup-confirm project paths idempotently through IProjectStore
type: implementation
state: verifying
---

## Expected result

Setup confirmation resolves an already-linked project path (including a Git worktree alias) without attempting a duplicate project-path insert; an unregistered path still creates or links its project. The direct `/api/projects` creation behavior remains unchanged.

## Scenarios

1. Confirming setup for a path already registered as the primary path succeeds and keeps the same project ID.
2. Confirming setup for an already-linked worktree path succeeds and keeps the existing project ID and primary path.
3. Confirming setup for an unregistered repository creates one project.
4. Calling direct project creation for an already-linked path continues to produce the existing conflict behavior.

## In scope

- Expose `ResolveOrCreateProjectAsync` through the BoyScout Application `IProjectStore` port.
- Use it from setup confirmation rather than checking only primary paths and then calling create.
- Add a focused test for exact-path idempotency and repository alias handling.

## Out of scope

- Changing direct `/api/projects` duplicate-path semantics.
- Changing setup preview behavior or installer file-writing rules.
- Reworking project persistence or other API routes.

## Primary risk

Setup confirmation may encounter an existing alias path that is not represented by `ProjectSummary.PrimaryPath`; the operation must return the existing project instead of reporting a conflict.

## Verification

- Focused EF-backed project-store test covering an already-linked path and a Git worktree alias.
- BoyScout.Core.Tests suite to temporary output.

## Verification result

- `EfProjectStorePreservesGitIdentityPrimaryPathReportCountsAndAiConsent`: passed 1/1 after exercising repeat resolution for both the primary path and a linked worktree path; direct duplicate creation still throws the existing conflict.
- Full `BoyScout.Core.Tests`: passed 366/366 to `C:\Users\DENIS~1.AKT\AppData\Local\Temp\opencode\BoyScout-ITR021-Full\`.
- BoyScout.Mcp and BoyScout.Api built to temporary output with 0 warnings/errors.
- Temporary API scenario on port 5198, isolated SQLite DB, and AI disabled: setup preview/confirm for an already-linked worktree alias succeeded; the project list retained the same project ID and primary path; direct duplicate project creation returned 409; setup confirmation for a separate repository created a second project. A separate primary-path confirmation kept the count unchanged at 3. A PowerShell assertion initially miscounted a serialized array wrapper; a direct `GET /api/projects` confirmed the two records from the earlier scenario.

## Adjacent discovery

- `POST /api/setup/preview` compares only `ProjectSummary.PrimaryPath`. Runtime preview for an already-linked worktree alias included the “register repository as a site project” setup file even though that alias was already linked. This is outside ITR-021 scope. The Boy Scout Report MCP tools are not exposed in this session, so duplicate lookup/report submission could not be performed; do not treat this as reported.
