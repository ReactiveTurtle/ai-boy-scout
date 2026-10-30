---
id: ITR-022
topic: Open an Angular TodoGraph workspace by internal project ID
type: implementation
state: verifying
---

## Expected result

Selecting a project opens an Angular route keyed by its internal TodoGraph project ID and displays that project's metadata, tasks, and pending candidates without conflating projects that share an upstream source ID.

## Scenarios

1. Opening `/todo-graph/projects/:projectId` loads the project by `ProjectRepository.id`, plus tasks and candidates filtered with that same internal ID.
2. Projects sharing `sourceProjectId` still display their own project/task/candidate data.
3. An invalid project ID displays a validation error; an unknown project ID displays not found, both with a route back to projects.
4. Empty task/candidate results display empty states; API failure displays a retryable error.
5. The page links to the existing vanilla full workflow and graph while their actions are not yet migrated.

## In scope

- Add the Angular project workspace route and read-only project/task/pending-candidate view.
- Change Angular project cards to navigate to the internal-ID workspace route.
- Add typed TodoGraph API reads for tasks and candidates.
- Preserve access to the legacy full workflow and graph during incremental migration.

## Out of scope

- Task or candidate mutation actions, clarification forms, graph rendering, settings, and automation controls.
- Changes to TodoGraph API contracts or persistence.

## Primary risk

The API exposes both internal `id` and source `sourceProjectId`; workspace route resolution and filtered reads must consistently use internal `id`.

## Verification

- Angular production build.
- Headless browser check of project navigation, rendering, identity isolation, and loading/error states with stubbed API responses.
- Focused existing project-identity test for internal-ID task/candidate filtering.

## Topic evolution

- A temporary TodoGraph host was initially considered to verify API reads. Inspection showed its startup unconditionally writes Data Protection keys under LocalApplicationData, outside the isolated DB path. To keep verification within the workspace/temp boundaries, use browser-level mocked API responses and the focused existing CandidateStore identity test instead; actual cross-origin API calls remain unverified.

## Verification result

- `npm run build`: passed; the existing `app.scss` style budget warning remains (26.97 kB vs 26 kB).
- `SameSourceIdsInDifferentNamespacesHaveIsolatedInternalProjectsAndTaskData`: passed 1/1; project/task/candidate filtering remains isolated by internal project ID even when source IDs are equal.
- Headless Playwright against the Angular dev server with stubbed API responses passed: project-card navigation used internal ID 11 despite a duplicate source ID; the correct task/candidate rendered; the other project's task did not; empty task/candidate states, unknown/invalid IDs, API error, retry, and legacy workflow link passed.
- Actual cross-origin HTTP calls to TodoGraph were not exercised; the browser used stubbed responses and the API host was not started for the LocalApplicationData reason above.
