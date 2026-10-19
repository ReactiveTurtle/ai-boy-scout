# TASK-003 - Shared Boy Scout storage and project setup

## Goal

Keep SQLite as the default and offer an optional Firebase Realtime Database shared by the site and MCP. Support named installations, explicit local and shared projects, confirmed migration, exclusive renewable processing leases, paged lists, and per-repository local installation.

## Success criteria

- the site and MCP use the same selected store;
- credentials remain server-side and never enter a repository;
- source and confirmer attribution is visible;
- local projects move only after explicit confirmation and verified transfer;
- one installation owns a processing lease and stale results cannot be saved;
- task and note lists load on demand;
- repository setup stays uncommitted.

## Constraints

- preserve human confirmation for tasks and per-machine consent for source inspection;
- preserve local SQLite data until remote transfer is verified;
- do not expose Firebase administrative credentials through the unauthenticated API;
- do not claim remote storage is active until API, worker, and MCP share its complete workflow state.

## Scope clarification

Choosing Firebase for an existing project will affect new records only. Old
SQLite notes and tasks stay accessible in the same project list. Moving old
data remains a separate, explicitly confirmed and verified operation. No
Firebase choice may be enabled until new note, proposal, task, worker, and MCP
operations and merged pagination are implemented together.

## Iterations

- ITR-001: record version one as Git root commit `11ccb97`;
- ITR-002: allow explicit creation of an empty local project;
- ITR-003: page tasks and limit workflow requests;
- ITR-004: enforce the agreed localhost-only API boundary before accepting Firebase credentials;
- ITR-005: register a repository's MCP and site shortcut without tracking files;
- ITR-006: add a typed RTDB transport, counters, and lease primitives;
- ITR-007: separate store interfaces, persist local connection fields and installation identity;
- ITR-008: preserve attribution in SQLite v10 and display it in the site;
- ITR-009: retain titled BoyScout OpenCode verification sessions;
- ITR-010: store SQLite v11 agent and human decision times and arrange the
  note details from observed evidence to consequence, hypothesis and review;
- ITR-011: republish the outdated MCP after backing up the database and verify
  migration and reconnection;
- ITR-012: store per-installation Firebase project paths and AI consent locally;
- ITR-013: expose a metadata-only Firebase project catalog and setup form;
- ITR-014: fence proposal writes within the same remote project CAS;
- ITR-015: snapshot a complete local project graph read-only and expose a safe transfer preview;
- ITR-016: carry optional comments through every return-to-queue path;
- ITR-017: store separately evidenced current consequences in task drafts and tasks;
- ITR-018: surface blocked AI processing and place project actions in a project context;
- next: implement and independently verify the complete remote store, local path bindings,
  and confirmed migration before enabling Firebase selection. The ETag lease cannot fence
  a separate write; the lease and result must be checked in the same conditional mutation.
  Proposal readiness now uses the same-project CAS, but the complete worker and
  migration are not implemented yet.
- ITR-019: persist the worker's ready, rejection, unverified, and failure outcomes under
  the current Firebase note lease. Firebase activation remains disabled until queueing,
  human review, and the production worker are connected.
- ITR-020: discover and atomically claim eligible Firebase notes into a `processing`
  proposal, respecting the selected local project binding and AI-read consent before invoking OpenCode.
- ITR-021: connect claimed Firebase proposals to the existing OpenCode generator, renew leases
  for long runs, and persist outcomes through the lease-fenced FirebaseTaskRepository methods.
- ITR-022: atomically confirm an obsolete Firebase recommendation as rejected, updating the
  proposal and note status together while preserving the rest of the project root.
- ITR-023: expose bound Firebase note, proposal, and human confirmation operations through
  the localhost API without enabling per-project storage yet.
- ITR-024: provide a Firebase notes/proposals review panel with human confirm/reject actions,
  separate from the existing SQLite history while mixed paging is not yet implemented.
- ITR-025: let a reviewer retry or clarify an active Firebase proposal with a required comment,
  atomically clearing the old result and returning it to the remote queue.
- ITR-026: support Firebase rejection-review requests, processing requeue, and restore; invalidate
  leases atomically when canceling in-flight work.
- ITR-027: implement confirmed-note reprocess with safe deletion of only an exclusive remote task.
- ITR-028: expose remote retry, clarification, rejection review, requeue, restore and reprocess
  transitions through project-scoped localhost API routes.
- ITR-029: add consent-aware Firebase review controls for rejection requests, retry/clarification,
  requeue, restore and reprocess to the remote note detail panel.
- next: expose remote task browse/status, then merge Firebase/SQLite notes and tasks in their
  existing workspaces before enabling per-project storage.
