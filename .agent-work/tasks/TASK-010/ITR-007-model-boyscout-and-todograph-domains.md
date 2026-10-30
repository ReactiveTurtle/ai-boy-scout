---
id: ITR-007
topic: Model BoyScout and TodoGraph domain/module boundaries
type: discovery
state: accepted
---

## Expected result

Record the core domain scenarios and ownership rules needed to design the two modules before moving API hosts or persistence.

## In scope

- Define the project/report/task identifiers and ownership boundaries.
- List end-to-end and migration scenarios that the architecture must preserve.
- Identify the remaining host and EF scope decisions for the user.

## Out of scope

- Creating/moving projects, changing service ports, or altering deployment scripts.
- Changing database schemas or adding EF packages.
- Implementing application/domain code before the host and EF scope are confirmed.

## Domain glossary

- **BoyScoutProjectId**: numeric ID local to one BoyScout data store; it is not globally unique.
- **ProjectNamespaceId**: stable ID of the BoyScout data store that assigned a BoyScoutProjectId; it is not the report-origin installation ID.
- **TodoGraphProjectId**: unique internal TodoGraph project identity used by TodoGraph API routes, tasks, raw submissions, and repository bindings.
- **BoyScoutReportId**: source report identity, scoped by ProjectNamespaceId when imported.
- **Candidate**: a TodoGraph intake record from BoyScout or a direct user raw item.
- **TodoGraphTask**: an implementation/workflow record owned by TodoGraph, distinct from a BoyScout task snapshot.
- **RepositoryBinding**: local repository path/code-host configuration owned by the TodoGraph project.
- **Process**: a TodoGraph aggregate for a built-in task workflow; it owns its stages, instructions, current stage, and transition history.

## Required scenarios

1. An accepted/planned BoyScout report and its source task snapshots publish to TodoGraph; retrying the same publication is idempotent.
2. Two BoyScout stores use the same BoyScoutProjectId and BoyScoutReportId values; imports remain distinct by ProjectNamespaceId.
3. A single BoyScoutProjectId is imported under two namespaces; TodoGraph assigns distinct TodoGraphProjectIds and scopes candidates, tasks, repository bindings, raw items, and branch actions to the chosen internal ID.
4. A user submits a raw item to a TodoGraphProjectId; it has user source and remains isolated from other projects.
5. Candidate clarification, task creation, branch approval, preflight, worktree preparation, development, rebase, commit, and MR/PR tracking remain owned by TodoGraph.
6. Existing task relationships, candidate/task links, repository bindings, worktree/branch state, MR state, and history survive migration.
7. A legacy repository binding is assigned only when its stored project identity resolves unambiguously; otherwise preserve it as unresolved instead of guessing.

## Module boundary proposal

- BoyScout owns its reports, BoyScout projects/tasks, review state, and the source database namespace.
- TodoGraph owns imported candidates, raw user requests, internal projects, task execution, and repository bindings.
- Modules exchange versioned integration contracts; they do not share EF entities or write each other's tables.
- Keep the current two API hosts/processes; do not consolidate ports/deployment in this task.
- Both modules use EF Core in their own Infrastructure layer, with independent DbContexts and migration histories; no cross-module DbContext or writes.
- Add a `Common` project only for genuinely shared primitives and versioned integration contracts. Do not put module domain entities, EF types, Firebase SDK types, or repository abstractions for one module there.
- Each module's Application layer owns its task/project persistence ports. Domain/Application code must not depend on SQLite, EF Core, or Firebase.
- Firebase is a BoyScout Infrastructure adapter behind BoyScout Application ports; the TodoGraph module retains its own persistence adapter and can choose another provider through its own composition root.
- Preserve separate SQLite data stores during migration; the existing BoyScout Firebase integration remains behind its Infrastructure adapter.
- The Angular UI stays on the BoyScout host for now and calls TodoGraph through its loopback API boundary.
- A task implementation process is owned by TodoGraph; it is not a Common or BoyScout domain entity.

## Open decisions

None. The user chose to retain two hosts and use EF Core in both modules.

## Topic checks

- Do not conflate report-origin installation metadata with the BoyScout database namespace.
- Do not use upstream numeric project IDs as TodoGraphProjectIds.
- Do not silently merge ambiguous legacy records or repository bindings.
- Do not describe the current deployment as a single-process modular monolith while both hosts remain separate.

## Human decisions

- Keep the two hosts.
- Apply EF Core to both BoyScout and TodoGraph persistence layers.
- Include a Common project, keep Firebase in Infrastructure, and keep task storage provider-independent through module-owned Application ports.
