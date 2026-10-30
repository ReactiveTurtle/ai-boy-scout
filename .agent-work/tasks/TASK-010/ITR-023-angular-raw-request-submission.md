---
id: ITR-023
topic: Submit a raw request from the Angular project workspace
type: implementation
state: verifying
---

## Expected result

A user can submit a non-empty raw request of up to 10,000 characters to the selected project's existing candidate queue from Angular, receive success/error feedback, and keep input on failure.

## Scenarios

1. Submitting valid text posts `{ text }` to `/api/projects/{internalProjectId}/raw-items`, clears the form on success, shows confirmation, and refreshes the pending candidate list.
2. Blank/whitespace text cannot be submitted; the server's 10,000-character limit is also reflected in the form.
3. API validation/not-found failures show an error, retain the user's text, and re-enable submission.
4. Submission is disabled while the request is in flight to prevent accidental duplicate posts.
5. Success feedback remains visible even if the background agent changes the candidate status before the list refresh.

## In scope

- Add a typed Angular API method for the existing raw-item endpoint.
- Add the request form and its validation/submission states to the Angular workspace.
- Refresh the workspace after successful submission.

## Out of scope

- Candidate answers, agent review, plan decisions, retries, and task creation actions.
- TodoGraph API contract or persistence changes.

## Primary risk

Candidate processing starts asynchronously after insertion, so success feedback must reflect the POST result and not depend on the candidate still appearing in the pending list.

## Verification

- Angular production build.
- Headless browser check with mocked API responses for request payload/project ID, success refresh, validation, error retention, and duplicate-submit prevention.
- Existing CandidateStore raw-item test for internal project association and queue behavior.

## Verification result

- `npm run build`: passed. Lazy-loading the workspace kept the initial bundle at 487.37 kB (below the 500 kB budget); existing `app.scss` warning remains at 26.97 kB vs 26 kB.
- `RawItemQuestionsPersistAcrossRoundsAndOnlyFinalCriteriaCreateLinkedTask`: passed 1/1 to temporary output. The first test invocation overlapped the Angular build and hit a stale compressed-static-asset race; sequential rerun passed.
- Headless Playwright against the Angular dev server with mocked responses passed: whitespace was not submitted; valid text posted to `/api/projects/11/raw-items` as `{ "text": "Export reports" }`; the submit button disabled in flight; success cleared the form and refreshed candidates; success remained visible when the refreshed candidate list was empty; HTTP 400 and 404 errors retained entered text and re-enabled submission.
- Actual HTTP calls to the live TodoGraph API and CORS behavior remain unverified; the browser used stubs and no standalone API host was started due its LocalApplicationData Data Protection key write.
